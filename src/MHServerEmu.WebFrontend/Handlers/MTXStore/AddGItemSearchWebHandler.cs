using System.Net;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.MTXStore
{
    /// <summary>
    /// Searches the items the add G panel can give for free by name.
    /// The game instance rejects the request unless MTXStore FreeItemsEnabled is on.
    /// </summary>
    public class AddGItemSearchWebHandler : WebHandler
    {
        private const int MaxQueryLength = 100;

        protected override async Task Post(WebRequestContext context)
        {
            ItemSearchRequest request = await context.ReadJsonAsync<ItemSearchRequest>();

            string query = request.Query?.Trim() ?? string.Empty;
            if (query.Length == 0 || query.Length > MaxQueryLength)
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            ServiceMessage.MTXStoreItemSearchResponse response = await GameServiceTaskManager.Instance.SearchFreeItemsAsync(request.Email, request.Token, query);

            context.StatusCode = response.StatusCode;
            if (response.StatusCode != (int)HttpStatusCode.OK)
                return;

            // Prototype ids don't fit in a JavaScript number, so they are sent as strings.
            ItemSearchResultItem[] items = (response.Items ?? [])
                .Select(item => new ItemSearchResultItem($"{item.ItemProtoRef}", item.Name, item.Category, item.Path))
                .ToArray();

            await context.SendJsonAsync(new ItemSearchResult(items, response.Truncated));
        }

        private readonly struct ItemSearchRequest
        {
            public string Email { get; init; }
            public string Token { get; init; }
            public string Query { get; init; }
        }

        private readonly record struct ItemSearchResultItem(string Id, string Name, string Category, string Path);
        private readonly record struct ItemSearchResult(ItemSearchResultItem[] Items, bool Truncated);
    }
}
