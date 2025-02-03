using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Text;
using System.Web;
using static RMI.LeadCallProxyAPI.ErrorManager;

namespace RMI.LeadCallProxyAPI {
    public class ProxyControllerEx : Controller {
        private const int _denominator = 100;
        private static int _splitCounter = 0;
        public const string ProxyRequestKey = "ProxyRequest";

        public static string Name => typeof(ProxyControllerEx).Name;
        public Uri RequestUri => this.Request.RequestUri();

        public IQueryCollection Query => this.Request.Query;
        public string QueryString => this.RequestUri.Query;

        public string UserAgent => this.Request.Headers.UserAgent.ToString();
        public string AuthKey => this.Request.Headers.GetValue("AuthKey");
        public ProxySettingsBase Proxy { get; private set; }
        public string ContentType => this.Request.ContentType;
        public string PostedData { get; private set; }

        public string Error { get; private set; }
        internal ErrorData CachedError => this.Proxy.ProxyUri.GetCachedError();
        public bool NoSlackMessages => this.QueryString.IsMatch("[?&](slack|alert|notify)=false");

        public bool IsLeadConduit => this.Proxy == Settings.ProxySettings.LeadConduit;
        public bool IsIPQS => this.Proxy == Settings.ProxySettings.IPQS;
        public bool PassThru => this.QueryString.IsMatch(@"[?&](test(only|ing)?|bypass|passthru)=true") ||
            this.QueryString.IsMatch(@"[?&]code=\d+") ||
            (this.IsInvoca_Proxy && Settings.ProxySettings.PassThru);

        public bool IsInvoca_Proxy => this.IsIPQS || this.IsLeadConduit || this.UserAgent.IsMatch("Invoca");
        public int SplitCounter => _splitCounter;

        public async Task<IActionResult> InvokeRequest() {
            this.HttpContext.Items.SafeAdd(Name, this);
            await this.SetProxyUrl();
            int? errorCode = this.Query["code"].ToInt();

            #region DEBUG
#if DEBUG
            if(this.QueryString.IsMatch("[?&]testsplit\b")) {
                string output = $"{this.Proxy.ProxyUri.Host} - {this.SplitCounter}";
                return new ContentResult {
                    StatusCode = (int)HttpStatusCode.OK,
                    ContentType = WebRequest.PlainTextContentType,
                    Content = output
                };
            }
#endif
            #endregion DEBUG
            
            if(this.Error.HasValue()) {
                throw new NotImplementedException("This should never happen");
            } else if(this.IsPaused()) {
                throw new NotImplementedException("This should never happen");
            }

            HttpMethod method = new HttpMethod(this.Proxy.Method);
            using(HttpRequestMessage proxyReq = new HttpRequestMessage(method, this.Proxy.ProxyUrl)) {
                this.HttpContext.Items.SafeAdd(ProxyRequestKey, proxyReq);
                if(method != HttpMethod.Get && this.PostedData.HasValue()) {
                    byte[] bytes = Encoding.UTF8.GetBytes(this.PostedData);
                    proxyReq.Content = new ByteArrayContent(bytes);
                    proxyReq.Content.Headers.Add("Content-Type", this.Proxy.ContentType);
                }

                if(errorCode.HasValue) {
                    if(this.IsLeadConduit) { errorCode = 200; }
                    throw new CustomException("Simulating Error Response", (HttpStatusCode)errorCode.Value);
                } else if(this.PassThru) {
                    throw new CustomException("Simulating OK Response", HttpStatusCode.OK);
                }
                HttpResponseMessage resp = await proxyReq.Send(this.Proxy.RequestTimeoutSeconds);
                return await this.UpdateResponse(resp);
            }
        }

        public async Task<IActionResult> UpdateResponse(HttpResponseMessage resp) {
            DateTime now = DateTime.Now;
            this.Proxy.ResponseTime = now;
            this.Proxy.ExecuteTimeSeconds = Math.Round(now.Subtract(this.Proxy.RequestTime).TotalSeconds, 2);
            this.Proxy.ResponseString = await resp.GetResponseString();

            IActionResult response;        
            if(this.IsInvoca_Proxy) {
                bool success = resp.IsSuccessStatusCode();
                this.UpdateCache(success);
                response = this.SaveDataToStorageBlob();
            } else {
                response = new ContentResult {
                    StatusCode = (int)resp.StatusCode,
                    ContentType = resp.Content.Headers.ContentType?.ToString(),
                    Content = this.Proxy.ResponseString
                };
            }

            if(AzureTelemetry.IsTelemetryEnabled) {
                AzureTelemetry.TrackMetric("Proxy-Response-Time", this.Proxy.ExecuteTimeSeconds);
                IDictionary<string, string> properties = new Dictionary<string, string>() {
                        { "Proxy-Url", this.Proxy.ProxyUrl },
                        { "Proxy-Response-Status", resp.ReasonPhrase },
                        { "Proxy-Response-Time", this.Proxy.ExecuteTimeSeconds.Format() },
                        { "Proxy-Paused", this.IsPaused().ToLower() },
                        { "Proxy-PassThru", this.PassThru.ToLower() }
                    };

                AzureTelemetry.TrackEvent("Proxy-Info", properties);
                AzureTelemetry.TrackEvent("Proxy-Response", this.Proxy.ResponseString.ToDictionary());
            }

            return response;
        }
        private async Task SetProxyUrl() {
            if(this.Request.IsCacheRequest()) { return; }
            DateTime now = DateTime.Now;
            Uri uri = this.RequestUri;
            if(this.IsInvoca_Proxy) {
                string body = await this.Request.GetRequestBody();
                JObject reqObject = JObject.Parse(body);
                string phone = reqObject.Value<string>("phone_1");
                string campaign;
                if(phone.HasValue()) {
                    campaign = reqObject.Value<string>("promo_description_rmi");
                    if(campaign.IsEmpty()) {
                        this.Error = "promo_description_rmi was not passed";
                        throw new CustomException(this.Error);
                    }
                } else {
                    this.Error = "phone_1 was not passed";
                    throw new CustomException(this.Error);
                }

                ProxySettingsBase proxy = null;
                if(!this.IsPaused()) {
                    int? split = this.Query["split"].ToInt();
                    bool splitValue = CheckSpitValue(split);
                    proxy = splitValue ? Settings.ProxySettings.IPQS : Settings.ProxySettings.LeadConduit;
                } else if(!Settings.ProxySettings.LeadConduit.ProxyUri.IsPaused()) {
                    proxy = Settings.ProxySettings.LeadConduit;
                }

                if(proxy == null) {
                    this.Error = "No proxy URL could be determined.";
                    throw new CustomException(this.Error);
                }

                if(proxy == Settings.ProxySettings.IPQS) {
                    campaign = HttpUtility.UrlEncode(campaign);
                    this.Proxy = proxy;
                    this.Proxy.ProxyUrl = $"{proxy.BaseUrl}/{phone}?promoCampaign={campaign}&country=US";
                } else { // LeadConduit
                    this.Proxy = proxy;
                    this.Proxy.ProxyUrl = proxy.BaseUrl;
                }

                this.Proxy.RequestTime = now;
                if(reqObject != null) {
                    reqObject["proxy_host"] = this.Proxy.ProxyUri.Host;
                    this.PostedData = reqObject.ToString(Formatting.None);
                }
            } else {
                WebRequest.PostToSlack($"URL: {uri.AbsoluteUri}");
            }
        }

        private static bool CheckSpitValue(int? percent) {
            int splitPercent = percent >= 0 ? percent.Value : Settings.ProxySettings.IPQS.SplitPercent;
            if(splitPercent <= 0) {
                _splitCounter = 0;
                return false;
            }
            if(splitPercent >= 100) {
                _splitCounter = 0;
                return true;
            }

            int modValue;
            if(splitPercent > 50) {
                modValue = _denominator / (_denominator - splitPercent);
            } else {
                modValue = _denominator / splitPercent;
            }

            int count = Interlocked.Increment(ref _splitCounter);
            bool result = count % modValue == 0;
            if(result) { _splitCounter = 0; }
            if(splitPercent > 50) { return !result; }
            return result;
        }

        private IActionResult SaveDataToStorageBlob() {
            const string na = "N/A";
            bool success = true;
            if(!this.PassThru) {
                JObject respObj = null;
                JObject posted = JObject.Parse(this.PostedData);
                try {
                    if(this.Proxy.ResponseString.HasValue()) {
                        respObj = JObject.Parse(this.Proxy.ResponseString);
                    }
                } catch {
                    // Ignore Error Here
                }

                if(this.IsLeadConduit) {
                    success = respObj == null || respObj.Value<string>("outcome") == "success";
                    posted["error"] = this.Error;
                } else {
                    success = respObj == null || respObj.Value<bool>("success");
                    bool valid = respObj?.Value<bool?>("valid") == true;

                    if(success && valid) {
                        int fraud_score = respObj.Value<int>("fraud_score");
                        bool recent_abuse = respObj.Value<bool?>("recent_abuse") == true;
                        bool risky = respObj.Value<bool?>("risky") == true;
                        bool do_not_call = respObj.Value<bool?>("do_not_call") == true;
                        bool leaked = respObj.Value<bool?>("leaked") == true;
                        bool spammer = respObj.Value<bool?>("spammer") == true;
                        bool active = respObj.Value<bool?>("active") == true;

                        posted["recent_abuse"] = recent_abuse;
                        posted["fraud_score"] = fraud_score;
                        posted["risky"] = risky;
                        posted["do_not_call"] = do_not_call;
                        posted["leaked"] = leaked;
                        posted["spammer"] = spammer;
                        posted["active"] = active;
                        success = !recent_abuse && (fraud_score < Settings.ProxySettings.IPQS.MaxFraudScore);
                    } else {
                        posted["recent_abuse"] = na;
                        posted["fraud_score"] = na;
                        posted["risky"] = na;
                        posted["do_not_call"] = na;
                        posted["leaked"] = na;
                        posted["spammer"] = na;
                        posted["active"] = na;
                        posted["error"] = this.Error ?? respObj?.Value<string>("message");
                    }
                    posted["valid"] = valid;
                }

                try {
                    posted["request_url"] = this.RequestUri.Host;
                    posted["deployment_slot"] = Settings.SlotName;
                    posted["build_version"] = Settings.BuildVersion;

                    posted["success"] = success;
                    posted["timestamp"] = this.Proxy.RequestTime;
                    posted["total_seconds"] = this.Proxy.ExecuteTimeSeconds;
                    posted["service_paused"] = this.IsPaused();

                    string dataqueueUrl = $"{Settings.DataQueueApiUrl}/{this.Proxy.DataTable}/";
                    WebRequest.SendNoWait(dataqueueUrl, HttpMethod.Put, posted.ToString(Formatting.None));
                } catch {
                    // Ignore Error Here
                }
            }
            if(success) {
                return this.Invoca_Response(HttpStatusCode.OK);
            }
            return this.Invoca_Response(HttpStatusCode.NoContent);
        }

        public IActionResult Invoca_Response(HttpStatusCode statusCode) {
            if(statusCode == HttpStatusCode.NoContent) {
                return new StatusCodeResult((int)statusCode);
            } else {
                return new ContentResult {
                    StatusCode = (int)HttpStatusCode.OK,
                    ContentType = WebRequest.JsonContentType,
                    Content = this.PostedData
                };
            }
        }
    }
}
