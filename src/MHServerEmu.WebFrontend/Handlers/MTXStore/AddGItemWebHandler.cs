using System.Net;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.MTXStore
{
    /// <summary>
    /// Gives free copies of an item from the add G panel.
    /// The game instance rejects the request unless MTXStore FreeItemsEnabled is on.
    /// </summary>
    public class AddGItemWebHandler : WebHandler
    {
        protected override async Task Post(WebRequestContext context)
        {
            AddFreeItemRequest request = await context.ReadJsonAsync<AddFreeItemRequest>();

            // Prototype ids don't fit in a JavaScript number, so the page sends them as strings.
            if (ulong.TryParse(request.Item, out ulong itemProtoRef) == false)
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            ServiceMessage.MTXStoreAddFreeItemResponse response = await GameServiceTaskManager.Instance.AddFreeItemAsync(request.Email, request.Token, itemProtoRef, request.Amount);

            context.StatusCode = response.StatusCode;
            if (response.StatusCode == (int)HttpStatusCode.OK)
                await context.SendJsonAsync(new AddFreeItemResult(response.Given));
        }

        private readonly struct AddFreeItemRequest
        {
            public string Email { get; init; }
            public string Token { get; init; }
            public string Item { get; init; }
            public int Amount { get; init; }
        }

        private readonly record struct AddFreeItemResult(int Given);
    }
}
