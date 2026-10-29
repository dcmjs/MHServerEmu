using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.MTXStore
{
    /// <summary>
    /// Grants free G from the add G panel. The game instance rejects the request unless MTXStore FreeGEnabled is on.
    /// </summary>
    public class AddGFreeWebHandler : WebHandler
    {
        protected override async Task Post(WebRequestContext context)
        {
            AddFreeGRequest request = await context.ReadJsonAsync<AddFreeGRequest>();

            ServiceMessage.MTXStoreAddFreeGResponse response = await GameServiceTaskManager.Instance.AddFreeGAsync(request.Email, request.Token, request.Amount);

            // Like the ES conversion, the result is communicated only via the HTTP status code.
            context.StatusCode = response.StatusCode;
        }

        private readonly struct AddFreeGRequest
        {
            public string Email { get; init; }
            public string Token { get; init; }
            public int Amount { get; init; }
        }
    }
}
