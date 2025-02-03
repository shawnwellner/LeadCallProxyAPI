using System.Collections.Specialized;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace RMI.LeadCallProxyAPI {
    public static class WebRequest {
        private const string DefaultSource = "RMI.LeadCallProxyAPI";
        public const string InsertHereKey = "INSERT_HERE";
        public const string URLEncodedContentType = "application/x-www-form-urlencoded";
        public const string HtmlContentType = "text/html";
        public const string PlainTextContentType = "text/plain";
        public const string JsonContentType = "application/json";

        #region PostToSlack
        public static void PostToSlack(string message) {
            var data = new {
                title = DefaultSource,
                message
            };
            PostToSlack(data);
        }
        public static void PostToSlack(object data) {
            Task.Factory.StartNew(async () => {
                try {
                    using(HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, Settings.SlackUrl)) {
                        byte[] bytes = Encoding.UTF8.GetBytes(data.ToJson());
                        req.Content = new ByteArrayContent(bytes);
                        req.Content.Headers.Add("Content-Type", $"{JsonContentType}; charset=utf-8");
                        //req.SendNoWait();

                        HttpResponseMessage resp = await req.Send();
                        string result = await resp.GetResponseString();
                        result.ToString();
                    }
                } catch(Exception ex) {
                    ex.ToString();
                }
            }).ConfigureAwait(false);
        }

        public static async Task PostToSlack(this Exception ex, HttpContext context) {
            ex.Source = DefaultSource;
            string sessionId = context.GetSessionId();
            
            ex.FillData(context.Request);

            var data = sessionId.GetSessionData();
            if(data != null) {
                var orgData = ex.Data.Clone(true);
                foreach(string k in data.Keys) {
                    if(k.Equals(InsertHereKey)) {
                        ex.Data.AddSpacer();
                        foreach(var d in orgData) {
                            ex.Data.SafeAdd(d.Key, d.Value);
                        }
                        //ex.Data.AddSpacer();
                    } else {
                        ex.Data.SafeAdd(k, data[k]);
                    }
                }
            }

            string json = ex.ToJson();
            await Post(Settings.SlackUrl, json);
        }

        private static void FillData(this Exception ex, HttpRequest req, HttpContext context = null) {
            if(null == ex || null == req) { return; }

            string postedData = null;
            context ??= req.HttpContext;

            string prefix = context == req.HttpContext ? null : "Proxy_";
            string url = req.GetRealUri().ToString();

            ProxyControllerEx controller = context.GetProxyController();

            if(prefix.HasValue() && controller != null) {
                string reason = null;
                string response = null;
                double seconds = controller.Proxy.ExecuteTimeSeconds;
                HttpStatusCode statusCode = HttpStatusCode.InternalServerError;
                if(ex is CustomException custEx) {
                    if(custEx.Response != null) {
                        statusCode = custEx.Response.StatusCode;
                        response = custEx.Response.GetResponseString().ContinueWith(t => t.Result).Result;
                    }
                } else if(ex is HttpRequestException reqEx) {
                    reason = reqEx.Message;
                    if(reqEx.InnerException is WebException webEx) {
                        statusCode = webEx.Status.ToHttpStatusCode();
                        reason = webEx.Message;
                    }
                }

                ex.Data.AddSpacer()
                       .SafeAdd($"{prefix}Info", string.Empty)
                       .SafeAdd("\t - Request-Url", url)
                       .SafeAdd("\t - Proxy-Url", controller.Proxy.ProxyUrl)
                       .SafeAdd("\t - Method", req.Method.ToString())
                       .SafeAdd("\t - Status", statusCode.AsString())
                       .SafeAdd("\t - Error", reason.IsEmpty(ex.Message))
                       .SafeAdd("\t - Paused", controller.IsPaused())
                       .SafeAdd("\t - Response-Time", $"{seconds} Seconds");

                if(response.HasValue()) {
                    ex.Data.SafeAdd($"\t - Response", response);
                }
                ex.Data.AddSpacer();
            } else {
                string userAgent = req.Headers?.UserAgent.ToString().IsEmpty("null");

                string remoteAddress = req.UserIPAddress();
                postedData = req.GetRequestBody().ContinueWith(t => t.Result).Result;
                ex.Data.SafeAdd("Request-Url", url)
                       .SafeAdd("Method", req.Method.ToString())
                       .SafeAdd("Remote-Address", remoteAddress)
                       .SafeAdd("User-Agent", userAgent)
                       .AddSpacer();
            }

            if(req.Headers.Count > 0) {
                ex.Data.SafeAdd($"{prefix}Headers", string.Empty);
                foreach(var h in req.Headers) {
                    try {
                        string value = string.Join(",", h.Value);
                        ex.Data.SafeAdd($"\t - {h.Key}", value);
                    } catch {
                        //Ignore Error Here
                    }
                }
            }

            if(postedData.HasValue()) {
                ex.Data.AddSpacer().SafeAdd($"{prefix}Posted", postedData);
            }

            if(prefix.IsEmpty()) {
                if(context.Items.TryGetValue(ProxyControllerEx.ProxyRequestKey, out object requestMessage)) {
                    HttpRequest proxyReq = (requestMessage as HttpRequestMessage).ToHttpRequest();
                    ex.FillData(proxyReq, context);
                }
            }
        }
        #endregion PostToSlack

        public static async Task<HttpResponseMessage> Post(string url, string data) {
            return await Send(url, HttpMethod.Post, data);
        }

        public static void SendNoWait(string url, HttpMethod method, string data = null) {
            Task.Factory.StartNew(async () => {
                var resp = await Send(url, method, data); //.ConfigureAwait(false);
                if(!resp.IsSuccessStatusCode()) {
                    PostToSlack($"Status: {resp.StatusCode} - {url}");
                }
            }).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> Send(string url, HttpMethod method, string data = null) {
            using(HttpRequestMessage req = new(method, url)) {
                req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                if(method.Method.IsMatch("POST|PUT") && data.HasValue()) {
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    req.Content = new ByteArrayContent(bytes);
                    req.Content.Headers.Add("Content-Type", $"{JsonContentType}; charset=utf-8");
                }
                return await req.Send();
            }
        }

        public static async Task<HttpResponseMessage> Send(this HttpRequestMessage req, int timeoutSeconds = 30) {
            var handler = new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = false
            };
            
            using(HttpClient client = new HttpClient(handler)) {
                //if(req.RequestUri.Host.IsMatch("rmi-datastore-api")) {
                if(req.RequestUri.CompareHostNames(Settings.DataQueueApiUri)) {
                    client.DefaultRequestHeaders.Add("AuthKey", Settings.Datastore_AuthKey);
                }
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                return await client.SendAsync(req);
            }
        }

        public static bool IsSuccessCode(this HttpStatusCode? code) {
            if(code == null) { return false; }
            int value = (int)code.Value;
            return value.Between(200, 299);
        }

        public static bool IsSuccessStatusCode(this HttpResponseMessage resp) {
            return resp?.IsSuccessStatusCode == true ||
                   resp.StatusCode.In(HttpStatusCode.Found, HttpStatusCode.Moved);
        }

        public static bool HasResponseBody(this HttpResponseMessage resp) {
            return resp?.IsSuccessStatusCode == true ||
                   resp.StatusCode == HttpStatusCode.InternalServerError;
        }

        public static async Task<string> GetResponseString(this HttpResponseMessage resp) {
            HttpContent content = resp?.Content;
            if(content != null && resp.HasResponseBody()) {
                var headers = content.Headers;
                Type type = headers.GetStream();
                if(type != null) {
                    byte[] bytes = await content.ReadAsByteArrayAsync();
                    return type.Decompress(bytes);
                } else {
                    return await resp.Content.ReadAsStringAsync();
                }
            }
            return null;
        }

        public static async Task<string> GetRequestBody(this HttpRequest request) {
            const string key = "RMI_RequestBody";
            const string gzip = "gzip";
            HttpContext context = request.HttpContext;
            if(request.Method.IsMatch("POST|PUT")) {
                if(context.Items.TryGetValue(key, out object body)) {
                    return body?.ToString();
                } else if(request.ContentLength > 0) {
                    using(var stream = request.Body) {
                        if(stream.CanSeek) {
                            stream.Seek(0, SeekOrigin.Begin);
                        }
                        using(var reader = new StreamReader(stream)) {
                            string data = await reader.ReadToEndAsync();

                            if(true == request.Headers.ContentEncoding.Contains(gzip)) {
                                data = data.Decompress();
                            }
                            context.Items.SafeAdd(key, data);
                            return data;
                        }
                    }
                }
            }
            return null;
        }

        private static Type GetStream(this HttpContentHeaders headers) {
            var encoding = headers.ContentEncoding;
            if(encoding.Contains("gzip")) {
                return typeof(GZipStream);
            } else if(encoding.Contains("br")) {
                return typeof(BrotliStream);
            } else if(encoding.Contains("deflate")) {
                return typeof(DeflateStream);
            }
            return null;
        }

        private static string Decompress(this Type type, byte[] bytes) {

            using(var msi = new MemoryStream(bytes)) {
                using(var mso = new MemoryStream()) {
                    using(var gs = Activator.CreateInstance(type, msi, CompressionMode.Decompress, false) as Stream) {
                        gs.CopyTo(mso);
                    }
                    return Encoding.UTF8.GetString(mso.ToArray());
                }
            }
        }

        private static string Decompress(this string data) {
            try {
                //string bytes Encoding.UTF8.GetBytes(data)
                byte[] bytes = Convert.FromBase64String(data);
                using(var msi = new MemoryStream(bytes)) {
                    using(var mso = new MemoryStream()) {
                        using(var gs = new GZipStream(msi, CompressionMode.Decompress, false)) {
                            gs.CopyTo(mso);
                        }
                        return Encoding.UTF8.GetString(mso.ToArray());
                    }
                }
            } catch(Exception ex) {
                return ex.Message;
            }
        }

        public static string Compress(this string data) {
            try {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                using(var mso = new MemoryStream()) {
                    using(var gs = new GZipStream(mso, CompressionMode.Compress)) {
                        //gs.CopyTo(mso);
                        gs.Write(bytes, 0, bytes.Length);
                    }
                    return Convert.ToBase64String(mso.ToArray());
                }
            } catch(Exception ex) {
                return ex.Message;
            }
        }

        public static Uri BuildUrl(string baseUrl, string appendPath) {
            if(Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri)) {
                string path = uri.GetLeftPart(UriPartial.Path).RegExReplace("/$", string.Empty);
                if(Uri.TryCreate(appendPath, UriKind.Relative, out Uri appendUri)) {
                    path = $"{path}{appendUri.OriginalString.RegExReplace("/{2,}", "/")}";
                    UriBuilder builder = new UriBuilder(path);
                    if(uri.Query.HasValue()) {
                        if(builder.Query.HasValue()) {
                            NameValueCollection appendQS = HttpUtility.ParseQueryString(builder.Query);
                            NameValueCollection qs = HttpUtility.ParseQueryString(uri.Query);
                            qs.Add(appendQS);
                            builder.Query = qs.ToString();
                        } else {
                            builder.Query = uri.Query.RegExReplace(@"^\?", string.Empty);
                        }
                    }
                    return builder.Uri;
                } else {
                    throw new CustomException($"{appendPath} is not a valid URL");
                }
            }
            throw new CustomException($"{baseUrl} is not a valid URL");
        }
        public static async Task<bool> TestUrl(string url) {
            try {
                HttpMethod method = HttpMethod.Head;
                if(url.IsMatch(@"\.azurewebsites\.net")) {
                    method = HttpMethod.Get;
                    url += "/ping/";
                }

                using(HttpRequestMessage req = new HttpRequestMessage(method, url)) {
                    using(HttpClient client = new HttpClient()) {
                        var resp = await client.SendAsync(req);
                        resp.EnsureSuccessStatusCode();
                        return resp.IsSuccessStatusCode();
                    }
                }
            } catch {
                return false;
            }
        }
    }

    /*public class InternalRequest : HttpRequestMessage {
        public InternalRequest() {
            this.Method = HttpMethod.Get;
            this.ContentType = WebRequest.JsonContentType;
            this.TimeoutSeconds = 30;
        }

        public InternalRequest(ProxyController controller) : this() {
            this.Controller = controller;
            //this.Headers = controller.Request.Headers;
            
            Uri uri = controller.RequestUri;
            this.Url = uri.AbsoluteUri;
            this.Method = new HttpMethod(controller.Request.Method);
            this.AuthKey = controller.AuthKey;
        }

        public ProxyController Controller { get; private set; }

        //public IHeaderDictionary Headers { get; private set; }
        public string AuthKey { get; private set; }

        public int TimeoutSeconds { get; set; }

        public string Url { get; set; }
        //public HttpMethod Method { get; set; }
        public string Data { get; set; }
        public string ContentType { get; set; }
    }*/
}
