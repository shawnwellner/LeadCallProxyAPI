using Microsoft.AspNetCore.Mvc;

namespace RMI.LeadCallProxyAPI.Controllers {
    public class ProxyController : LeadCallProxyAPI.ProxyControllerEx {

        [HttpPost("/")]
        public async Task<IActionResult> Index() {
            return await this.InvokeRequest();
        }
    }
}
