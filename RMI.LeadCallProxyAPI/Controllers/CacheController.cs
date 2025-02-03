using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Net;
using static RMI.LeadCallProxyAPI.ErrorManager;

namespace RMI.LeadCallProxyAPI.Controllers {
    public class CacheController : Controller {
        [HttpGet, Route("cache/{method}")]
        public IActionResult CacheInfo(string method) {
            if(this.Request.IsAdmin()) {
                if(method.IsMatch("details|info")) {
                    var items = ExMethods.GetCachedItems<ErrorData>();
                    if(items.Any()) {
                        return new ContentResult() {
                            StatusCode = (int)HttpStatusCode.OK,
                            ContentType = WebRequest.JsonContentType,
                            Content = items.ToJson()
                        };
                    } else {
                        return new RedirectResult($"/cache/status/");
                    }
                }
                return this.CheckCache();
            }
            return new StatusCodeResult((int)HttpStatusCode.Unauthorized);
        }

        private IActionResult CheckCache() {
            Uri reqUri = this.Request.RequestUri();
            if(reqUri.AbsolutePath.IsMatch("clear")) {
                if(this.Request.Query.TryGetValue("host", out StringValues values)) {
                    string host = values.SingleOrDefault();
                    Uri uri = new Uri($"http://{host}");
                    uri.ClearCache();
                } else {
                    Settings.Initialize(null);
                    ErrorManager.ClearCache();
                }
                return new RedirectResult($"/cache/info/");
            } else {
                string response = ErrorManager.ToHtml();
                return new ContentResult() {
                    StatusCode = (int)HttpStatusCode.OK,
                    ContentType = WebRequest.HtmlContentType,
                    Content = response
                };
            }
        }
    }
}
