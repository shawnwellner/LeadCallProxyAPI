using Newtonsoft.Json;
using System.Text;

namespace RMI.LeadCallProxyAPI {
    internal static class ErrorManager {
        private static object _lock = new object();

        #region ErrorData Class
        internal class ErrorData {
            private ErrorData(Uri uri) {
                this.ProxyUri = uri;
            }

            public static explicit operator ErrorData(ProxyControllerEx controller) {
                //HttpRequestMessage req = proxy.Request;
                Uri uri = controller.RequestUri;

                string resetUrl = new UriBuilder(uri.Scheme, uri.Host, uri.Port) {
                    Path = $"cache/clear/",
                    Query = $"host={controller.Proxy.ProxyUri.Host}"
                }.ToString();

                string statusUrl = new UriBuilder(uri.Scheme, uri.Host, uri.Port) {
                    Path = $"cache/status/",
                }.ToString();

                var data = new ErrorData(controller.Proxy.ProxyUri) {
                    Count = 1,
                    StatusUrl = statusUrl,
                    ResetUrl = resetUrl,
                    RequestHost = uri.Host,
                    SplitCounter = controller.SplitCounter,
                    Timestamp = DateTime.Now
                };
                data.Key.Cache(data, Settings.ProxySettings.ResetPauseMinutes);
                return data;
            }

            public static void SlackResume(Uri uri) {
                new ErrorData(uri).SlackPause(false);
            }

            [JsonProperty("proxy_host")]
            public string Key => this.ProxyUri.Host;

            [JsonProperty("request_host")]
            public string RequestHost { get; private set; }

            [JsonIgnore]
            public string StatusUrl { get; private set; }

            [JsonIgnore]
            public string ResetUrl { get; private set; }

            [JsonProperty("prev_error")]
            public TimeSpan TimeSpan => DateTime.Now.Subtract(this.Timestamp);

            [JsonProperty("error_count")]
            public int Count { get; private set; }

            [JsonProperty("errors_remaining")]
            public int ErrorsRemaining => Settings.ProxySettings.MaxRequestErrorCount - this.Count;

            [JsonIgnore]
            public double RespSeconds { get; private set; }

            [JsonProperty("resp_seconds")]
            public string RespSecondsStr => this.RespSeconds.Format();

            [JsonProperty("resume_time")]
            public string ResumeTime => this.Timestamp.AddMinutes(Settings.ProxySettings.ResetPauseMinutes).Subtract(DateTime.Now).Format();

            [JsonProperty("split_counter")]
            public int SplitCounter { get; set; }

            [JsonProperty("paused")]
            public bool Paused => this.Count >= Settings.ProxySettings.MaxRequestErrorCount;
            //public bool Paused => this.ProxyUri.IsPaused();

            private DateTime Timestamp { get; set; }

            /*[JsonIgnore]
            public bool CanClear => DateTime.Now.Subtract(this.Timestamp).TotalMinutes > Settings.Reset_Pause_Minutes;
            */

            [JsonIgnore]
            public Uri ProxyUri { get; private set; }

            public ErrorData Update(double seconds, bool incrementCount) {
                DateTime now = DateTime.Now;
                this.RespSeconds = seconds;
                //this.AvgSeconds = (this.AvgSeconds + seconds) / this.Count;
                //this.TimeSpan = now.Subtract(this.Timestamp);

                if(incrementCount) {
                    this.Count++;
                    this.Timestamp = now;
                }
                return this;
            }
        }
        #endregion

        #region IsPaused
        public static IDictionary<string, bool> _paused = new Dictionary<string, bool>();

        public static ErrorData GetCachedError(this Uri uri, bool clearCache = false) {
            string cacheKey = uri.Host;
            bool isPaused = _paused.ContainsKey(cacheKey);
            if(cacheKey.TryGetCachedItem(out ErrorData data)) {
                if(clearCache) {
                    cacheKey.RemoveCache();
                    data.SlackPause(false);
                } else if(isPaused && !data.Paused) {
                    data.SlackPause(false);
                }
                return data;
            } else if(isPaused) {
                ErrorData.SlackResume(uri);
            }
            return null;
        }

        public static bool CompareHostNames(this Uri uri, Uri compare) {
            if(uri == null) { return false; }
            if(compare == null) { return false; }
            try {
                if(uri.Host.EqualsIgnoreCase(compare.Host)) {
                    return uri.Port == compare.Port;
                }
            } catch { 
                // Ignore Error
            }
            return false;
        }

        public static bool IsPaused(this Uri uri) {
            if(uri == null) { return false; }
            lock(_lock) {
                return uri.GetCachedError()?.Paused == true;
            }
        }

        public static bool IsPaused(this ProxyControllerEx controller) {
            return controller.Proxy?.ProxyUri.IsPaused() ?? Settings.ProxySettings.IPQS.ProxyUri.IsPaused();
        }
        #endregion

        public static void ClearCache(this Uri uri) {
            uri.GetCachedError(true);
        }

        public static void ClearCache() {
            foreach(var data in ExMethods.GetCachedItems<ErrorData>()) {
                _paused.SafeRemove(data.Key);
                data.Key.RemoveCache();
            }
        }

        public static void UpdateCache(this ProxyControllerEx controller, bool isSuccessCode) {
            lock(_lock) {
                ErrorData data = controller.CachedError;
                if(data != null) {
                    bool incrementError = !isSuccessCode;
                    data.Update(controller.Proxy.ExecuteTimeSeconds, incrementError);
                    data.SplitCounter = controller.SplitCounter;
                } else if(isSuccessCode) {
                    return;
                } else {
                    data = (ErrorData)controller;
                }

                if(!controller.NoSlackMessages) {
                    if((data.Count % Settings.ProxySettings.MaxRequestErrorCount) == 0) {
                        data.SlackPause(true);
                    }
                }
            }
        }

        #region SlackMessage
        private static dynamic GetFields(this ErrorData data, bool paused) {
            var fields = new object[] {
                new {
                    type = "mrkdwn",
                    text = $"Proxy-Host: *{data.ProxyUri.Host}*"
                }, new {
                    type = "mrkdwn",
                    text = $"Request-Host: *{data.RequestHost}*"
                }
            };

            if(paused) {
                return fields.Concat(new object[] {
                    new {
                        type = "mrkdwn",
                        text = $"Last-Error: *{data.TimeSpan.Format()}*"
                    }, new {
                        type = "mrkdwn",
                        text = $"Resume-Time: *{data.ResumeTime}*"
                    }, new {
                        type = "mrkdwn",
                        text = $"Total-Errors: *{data.Count}*"
                    }
                }).Cast<object>().ToArray();
            }

            return fields;
        }

        private static void SlackPause(this ErrorData data, bool paused) {
            const string success = "#2eb886";
            const string danger = "#dc3545";

            string header_text, color;
            if(paused) {
                _paused.SafeAdd(data.Key, true);
                header_text = $"{data.Key} is *PAUSED!!!*";
                color = danger;
            } else {
                _paused.SafeRemove(data.Key);
                header_text = $"{data.Key} is *RESUMED!!!*";
                color = success;
            }

            var blocks = new object[] {
                new {
                    type = "section",
                    fields = data.GetFields(paused)
                }
            };

            if(paused) {
                blocks = blocks.Concat(new object[] {
                    new { type = "divider" },
                    new {
                        type = "actions",
                        elements = new object[] {
                            new {
                                type = "button",
                                style = "primary",
                                value = "check_proxy_status",
                                url = data.StatusUrl,
                                text = new {
                                    type = "plain_text",
                                    text = "Check Status"
                                }
                            },
                            /*new {
                                type = "button",
                                style = "danger",
                                value = "clear_proxy_status",
                                url = data.ResetUrl,
                                text = new {
                                    type = "plain_text",
                                    text = "Clear Cache"
                                }
                            }*/
                        }
                    }
                }).Cast<object>().ToArray();
            }

            var attachments = new object[] {
                new {
                    color,
                    blocks
                }
            };
            var obj = new {
                channel = Settings.Pause_AlertChannel,
                title = "Proxy API Service",
                text = header_text,
                icon_url = "https://johnjayrec.nyc/wp-content/uploads/2016/02/logo_api.png",
                //icon = "https://cdn.rmiatl.org/cdn/img/slack/rmi-ipqs.png",
                attachments
            };
            WebRequest.PostToSlack(obj);
        }
        #endregion SlackMessage

        #region ToHtml
        private static string ToHtml(this ErrorData data) {
            string status = data.Paused ? "Paused" : "Active";
            return $@"
<section>
    <h3>{data.Key} - <strong class='{status.ToLower()}'>{status}!</strong></h3>
    <div class='left-margin'>
        <div><span>Total Errors:</span><span><b>{data.Count}</b></span></div>
        <div><span>Errors Till Paused:</span><span><b>{data.ErrorsRemaining}</b></span></div>
        <div><span>Last Error:</span><span><b>{data.TimeSpan.Format()}</b></span></div>
        <div><span>Resume Time:</span><span><b>{data.ResumeTime}</b></span></div>
        <div><a class='btn danger' href='{data.ResetUrl}' title='This will remove this item from the cache.'>Clear Cache</a></div>
    </div>
</section>
";
        }

        public static string ToHtml() {
            StringBuilder html = new StringBuilder();
            IList<string> htmlData = new List<string>();
            var cached = ExMethods.GetCachedItems<ErrorData>();
            if(cached?.Count() > 0) {
                foreach(ErrorData data in cached) {
                    htmlData.Add(data.ToHtml());
                }
            } else {
                htmlData.Add("Nothing is cached or paused");
            }

            html.Append($@"
<html>
	<head>
		<meta charset='utf-8'>
		<meta name='viewport' content='width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=1' data-tf-id='8'>
        <title>Proxy Cache Status</title>
        <link href='https://cdn.rmiatl.org/cdn/css/techinfo.css' rel='stylesheet' />
        <style>
            div.left-margin > div {{
                display: grid;
                grid-template-columns: 170px 146px;
                grid-gap: 20px;
            }}
            div.left-margin > div span:first-child {{ text - align: right; }}
            h3 > strong.active {{ color: #00ff00; }}
            h3 > strong.paused {{ color: #ff0000; }}
        </style>
	</head>
	<body>
        <div class='container'>
            {string.Join("<hr/>", htmlData)}
        </div>
    </body>
<html>");

            return html.ToString();
        }
        #endregion
    }
}
