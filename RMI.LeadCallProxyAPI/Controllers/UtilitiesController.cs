using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;

namespace RMI.LeadCallProxyAPI.Controllers {
    public class UtilitiesController : Controller {
        [HttpGet("throw")]
        public IActionResult Throw() {
            throw new Exception("Test Exception");
        }

        [HttpGet("ping")]
        public async Task<IActionResult> Ping() {
            const string pong = "pong";

            Uri uri = this.Request.RequestUri();
            if(uri.Query.IsMatch(@"\?(all|test)") && this.Request.IsAdmin()) {
                StringBuilder report = new StringBuilder();
                report.Append("<head><meta name='color-scheme' content='dark light'></head>");
                report.AppendLine("<style>body { margin: 50px; }\nb { color:red; }\nb.pass { color:green; }</style>");
                report.AppendLine("<h3>Tested Connections</h3>");

                string[] internalUrls = [
                    Settings.DataQueueApiUrl,
                    Settings.SlackUrl,
                    Settings.ProxySettings.IPQS.BaseUrl,
                    Settings.ProxySettings.LeadConduit.BaseUrl,
                    "https://www.google.com"
                ];

                foreach(string url in internalUrls) {
                    string testUrl = url.DomainOnly();
                    bool success = await WebRequest.TestUrl(testUrl);
                    if(success) {
                        report.Append($"<b class='pass'>PASS</b> - {testUrl}");
                    } else {
                        report.Append($"<b>FAIL</b> - {testUrl}");
                    }
                    report.AppendLine("</br>");
                }

                return new ContentResult {
                    StatusCode = (int)HttpStatusCode.OK,
                    ContentType = WebRequest.HtmlContentType,
                    Content = report.ToString()
                };
            }

            return new ContentResult {
                StatusCode = (int)HttpStatusCode.OK,
                ContentType = WebRequest.PlainTextContentType,
                Content = pong
            };
        }

        #region TechInfo
        //Authorize
        [HttpGet("/"), HttpGet("techinfo")]
        public async Task<IActionResult> TechInfo() {
            if(!this.Request.IsAdmin()) {
                return new ContentResult {
                    StatusCode = (int)HttpStatusCode.Forbidden,
                    ContentType = WebRequest.PlainTextContentType,
                    Content = "Forbidden"
                };
            }
            return await Task.Factory.StartNew(() => {
                StringBuilder buffer = new StringBuilder();
                string scmLink = string.Empty;
                if(Settings.ScmUrls.HasValue()) {
                    var urls = Settings.ScmUrls;
                    scmLink = $@"
            <div><a href='{urls[0]}' target='_blank'>Azure App Service</a></div>
            <div><a href='{urls[1]}' target='_blank'>Environment Details</a></div>";

                }

                buffer.AppendLine($@"
<!DOCTYPE html>
<html>
    <head>
        <meta charset=""utf-8"">
		<meta name=""viewport"" content=""width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=1"" />
        <title>Tech Info</title>
        <link href=""https://cdn.rmiatl.org/cdn/css/techinfo.css"" rel=""stylesheet"" />
        <style>
            a:link, a:visited {{ color: #007bff; text-decoration: none; }}
            .true {{ color: #00ff00; }}
            .false {{ color: #ff0000; }}
        </style>
    </head>
    <body>
        <section>
            <h3>Info</h3>
            <div class='left-margin'>
                <div>SlotName: {Settings.SlotName}</div>
                <div>AuthKey: {Settings.AuthKey}</div>
                <div>Datastore_AuthKey: {Settings.Datastore_AuthKey}</div>
                <div>Pause_Channel: {Settings.Pause_AlertChannel}</div>
                <div>LastUpdated:    {Settings.LastUpdated}</div>
                <div>CurrentTime: {DateTime.Now.FullFormat()}</div>
                <div>TimeZone: {TimeZoneInfo.Local.DisplayName}</div>
                {scmLink}
            </div>
        </section>
        <hr/>
        <section>
            <h3>Headers</h3>
            <div class='left-margin'>
                {this.Headers}
            </div>
        </section>
    </body>
</html>
");
                return new ContentResult {
                    StatusCode = (int)HttpStatusCode.OK,
                    ContentType = WebRequest.HtmlContentType,
                    Content = buffer.ToString()
                };
            });
        }

        private string Headers {
            get {
                const string cookiePattern = "Cookie";
                StringBuilder buffer = new StringBuilder();
                char[] splitOn = [';'];
                string value;

                foreach(var h in this.Request.Headers) {
                    value = string.Join(",", h.Value);
                    if(h.Key.IsMatch(cookiePattern)) {
                        buffer.AppendLine($"            <h4>{h.Key}s:</h4>");
                        foreach(string val in value.Split(splitOn, StringSplitOptions.RemoveEmptyEntries)) {
                            buffer.AppendFormat("           <div class='left-margin'>{0}</div>", val.Trim());
                        }
                    } else {
                        buffer.AppendLine($"            <div>{h.Key} = {value}</div>");
                    }
                }
                return buffer.ToString();
            }
        }
        #endregion
    }
}
