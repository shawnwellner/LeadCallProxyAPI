using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Primitives;
using System;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Text;

namespace RMI.LeadCallProxyAPI {
    public class RequestHandler {
        private readonly RequestDelegate _next;
        //private readonly ILogger _logger;

        private RequestHandler() { }
        public RequestHandler(RequestDelegate next) {
            this._next = next;
            //_logger = log;
        }

        public async Task InvokeAsync(HttpContext context) {
            try {
                if(context.Request.IsAzureRequest()) {
                    context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                } else {
                    AzureTelemetry.SetTelemetry(context);
                    if(context.Request.Path.Value.IsMatch("favicon")) {
                        await this.SendFile(context, "favicon.ico");
                    } else if(IsAllowed(context)) {
                        await this._next(context);
                    } else {
                        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    }
                }
            } catch(Exception ex) {
                //this._logger.LogError(ex, ex.Message);
                AzureTelemetry.TrackException(ex);
                await ex.PostToSlack(context).ConfigureAwait(true);
                if(context.Items.ContainsKey(ProxyControllerEx.Name)) {
                    await CustomException.HandleException(ex, context);
                } else {
                    await SendResponse(context, ex);
                }
            } finally {
                await context.Response.CompleteAsync();
            }
        }

        private static bool IsAllowed(HttpContext context) {
            context.CacheSession();
            var ep = context.GetEndpoint();
            if(ep == null) {
                return false;
            } else if(context.Request.Headers.TryGetValue("AuthKey", out StringValues values)) {
                string authkey = values.SingleOrDefault();
                if(authkey.EqualsIgnoreCase(Settings.AuthKey)) {
                    return true;
                }
            } else if(context.Request.IsAdmin()) {
                return true;
            }

            string prefix = $"{Settings.AssemblyNameSpace}.Controllers.".RegExEscape();
            //return ep.DisplayName.IsMatch($"{prefix}(UtilitiesController|CacheController)");
            return ep.DisplayName.IsMatch($"{prefix}UtilitiesController");
        }

        /*private static bool IsSigninRequest(HttpContext context) {
            var req = context.Request;
            return req.Method == "POST" && req.Path.Value.Contains("signin");
        }*/

        private static async Task SendResponse(HttpContext context, Exception ex) {
            const string error = "Unexpected Error Occured";
            try {
                string output = null;
                string contentType = WebRequest.JsonContentType;
                int code = (int)HttpStatusCode.InternalServerError;
                var request = context.Request;
                if(request.IsAdmin() && !request.Path.Value.IsMatch("^/throw")) {
                    if(ex is HttpRequestException hex) {
                        code = (int?)hex.StatusCode ?? code;
                    }
                    output = ex.Data.GetItem<string>("Error-ResponseBody");
                    if(output.IsEmpty()) {
                        output = ex.ToString();
                        contentType = WebRequest.PlainTextContentType;
                    }
                } else {
                    output = new { error, success = false }.ToJson();
                    await ex.PostToSlack(context).ConfigureAwait(true);
                }

                var response = context.Response;
                response.StatusCode = code;
                response.ContentType = contentType;
                response.ContentLength = output.Length;

                await response.Body.WriteAsync(Encoding.UTF8.GetBytes(output));
                await response.Body.FlushAsync();
            } catch {
                //Ignore
            }
        }

        private async Task SendFile(HttpContext context, string file_name) {
            try {
                var baseDirectory = Directory.GetCurrentDirectory();
                //string downloadPath = Settings.Configuration.GetSection("www").Get<string>();
                string path = Path.Combine(baseDirectory, "www");
                if(!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }
                path = Path.Combine(path, file_name);
                if(File.Exists(path)) {
                    await context.Response.SendFileAsync(path);
                    return;
                } else {
                    string url = $"https://cdn.rmiatl.org/cdn/img/{file_name}";
                    using(HttpClient client = new HttpClient()) {
                        HttpResponseMessage result = await client.GetAsync(url);
                        if(result.IsSuccessStatusCode) {
                            var fileBytes = await result.Content.ReadAsByteArrayAsync();
                            //var contentType = result.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                            await File.WriteAllBytesAsync(path, fileBytes);
                            await context.Response.SendFileAsync(path);
                            return;
                        }
                    }
                }
            } catch {
                //Ignore
            }
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }
    }
}