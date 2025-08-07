using CryptoBlade.Strategies.Wallet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoBlade.Controllers
{
    [ApiController, Route("api/[controller]"), Authorize]
    public class WalletController(IWalletManager wallet) : ControllerBase
    {
        private readonly IWalletManager _wallet = wallet;

        [HttpGet]
        public ActionResult<Balance> Get()
        {
            return Ok(_wallet.Contract);
        }
    }
}
