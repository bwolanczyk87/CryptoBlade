using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoBlade.Controllers
{
    [ApiController, Route("api/[controller]"), Authorize]
    public class PositionsController(ICbFuturesRestClient rest) : ControllerBase
    {
        private readonly ICbFuturesRestClient _rest = rest;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Position>>> Get(CancellationToken ct)
        {
            var positions = await _rest.GetPositionsAsync(ct);
            return Ok(positions);
        }
    }
}
