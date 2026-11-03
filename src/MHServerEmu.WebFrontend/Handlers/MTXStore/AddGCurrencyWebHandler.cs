using System.Net;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.MTXStore
{
    /// <summary>
    /// Grants a free amount of a currency (credits, splinters, marks, etc.) from the add G panel.
    /// The game instance rejects the request unless MTXStore FreeCurrencyEnabled is on.
    /// </summary>
    public class AddGCurrencyWebHandler : WebHandler
    {
        protected override async Task Post(WebRequestContext context)
        {
            AddFreeCurrencyRequest request = await context.ReadJsonAsync<AddFreeCurrencyRequest>();

            // Prototype ids don't fit in a JavaScript number, so the page sends them as strings.
            if (ulong.TryParse(request.Currency, out ulong currencyProtoRef) == false)
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            ServiceMessage.MTXStoreAddFreeCurrencyResponse response = await GameServiceTaskManager.Instance.AddFreeCurrencyAsync(request.Email, request.Token, currencyProtoRef, request.Amount);

            context.StatusCode = response.StatusCode;
            if (response.StatusCode == (int)HttpStatusCode.OK)
                await context.SendJsonAsync(new AddFreeCurrencyResult(response.Balance));
        }

        private readonly struct AddFreeCurrencyRequest
        {
            public string Email { get; init; }
            public string Token { get; init; }
            public string Currency { get; init; }
            public int Amount { get; init; }
        }

        private readonly record struct AddFreeCurrencyResult(int Balance);
    }
}
