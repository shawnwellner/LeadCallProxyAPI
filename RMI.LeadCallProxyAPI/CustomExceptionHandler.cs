using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;

namespace RMI.LeadCallProxyAPI {
    #region CustomExceptionHandler
    /*[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class ExceptionHandlerAttribute : ExceptionFilterAttribute {
        private static int count = 0;

        */
    #endregion CustomExceptionHandler

    internal class CustomException : Exception {
        private string _stackTrace = null;

        public CustomException(string message) : this(message, null) { }

        public CustomException(string message, Exception innerException) : base(message, innerException) {
            this._stackTrace = BuildStackTrace();
        }

        public CustomException(HttpResponseMessage resp) : this(resp.ReasonPhrase) {
            this.Response = resp;
        }

        public CustomException(string message, HttpStatusCode code) : this(message, null) {
            this.Response = new HttpResponseMessage(code);
        }

        public override string StackTrace => this._stackTrace;

        [JsonIgnore]
        internal HttpResponseMessage Response { get; private set; }

        public static string ToJson(Exception ex) {
            const Formatting format = Formatting.None;
            string stackTrace;
            if(ex is CustomException) {
                stackTrace = ex.StackTrace;
            } else {
                stackTrace = BuildStackTrace(ex);
            }
            CleanData(ex);
            string json = JsonConvert.SerializeObject(ex, format);
            JObject jObj = JObject.Parse(json);
            jObj["StackTraceString"] = stackTrace;

            jObj.AddFirst(new JProperty("BuildVersion", Settings.BuildVersion));
            jObj.AddFirst(new JProperty("Environment", Settings.SlotName));
            return jObj.ToString(format);

        }

        public static string BuildStackTrace(Exception ex = null) {
            StackTrace st;
            if(ex == null) {
                st = new StackTrace(0, true);
            } else {
                st = new StackTrace(ex, true);
            }
            StringBuilder sb = new StringBuilder();
            string ns = Settings.AssemblyNameSpace;
            Type type = typeof(CustomException);
            foreach(var frame in st.GetFrames()) {
                string fileName = frame.GetFileName();
                //&& method.DeclaringType?.Namespace.Matches("^RMI") == true
                if(fileName.HasValue()) {
                    MethodInfo method = frame.GetMethod() as MethodInfo;
                    if(method != null && method.DeclaringType != type) {
                        string methodName = GetMethodText(method);
                        string pattern = ns.RegExEscape();
                        fileName = fileName.RegExReplace($@".*\\{pattern}", ns);
                        int line = frame.GetFileLineNumber();
                        sb.Append($" at {methodName}")
                          .Append($" in {fileName}")
                          .Append($":line {line}")
                          .AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        private static string GetMethodText(MethodInfo method) {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{method.ReturnType.Name} {method.Name}(");
            //method.ToString().Replace($"{method.DeclaringType.Namespace}.", "");
            foreach(var param in method.GetParameters()) {
                //sb.Append($"{param.ParameterType.Name} {param.Name}, ");
                sb.Append($"{param.ParameterType.Name} {param.Name}, ");
            }
            string text = sb.ToString().TrimEnd(',', ' ') + ")";
            return text;
        }

        private static void CleanData(Exception ex) {
            if(ex?.Data.Count == 0) { return; }

            string[] removeList = { "MS_LoggedBy" };
            foreach(string key in removeList) {
                if(ex.Data.Contains(key)) {
                    ex.Data.Remove(key);
                }
            }
        }

        public static async Task HandleException(Exception ex, HttpContext context) {
            HttpRequest req = context.Request;
            if(ex is TaskCanceledException && req.IsAzureRequest()) {
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return;
            }

            AzureTelemetry.TrackException(ex);
            HttpStatusCode? statusCode = null;
            IActionResult resp = null;

            ProxyControllerEx controller = context.GetProxyController();
            string userAgent = controller?.UserAgent ?? req.Headers.UserAgent.ToString();
            if(ex is CustomException custEx) {
                statusCode = HttpStatusCode.BadRequest;
                if(custEx.Response.HasValue()) {
                    statusCode = custEx.Response.StatusCode;
                    if(statusCode.In(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden)) {
                        resp = controller?.Invoca_Response(HttpStatusCode.NoContent);
                    } else {
                        ex.Data.SafeAdd("LogOnly", true);
                        resp = await controller.UpdateResponse(custEx.Response);

                        if(statusCode != HttpStatusCode.OK || Settings.ProxySettings.PassThru) {
                            statusCode = HttpStatusCode.Accepted;
                        }
                    }
                }
            }

            resp ??= controller.Invoca_Response(statusCode ?? HttpStatusCode.Accepted);
            
            /*if(!statusCode.IsSuccessCode()) {
                await ex.PostToSlack(context).ConfigureAwait(true);
            }*/

            if(resp is ContentResult result && result.StatusCode.HasValue) {
                context.Response.StatusCode = result.StatusCode.Value;
                context.Response.ContentLength = result.Content.Length;
                context.Response.ContentType = result.ContentType;
                await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(result.Content));
                await context.Response.Body.FlushAsync();
            } else {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            }

        }
    }
}
