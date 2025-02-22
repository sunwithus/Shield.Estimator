//SignalRHubs.cs

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;

namespace Shield.Estimator.Shared.Components.Modules._Shared
{
    public class ReplicatorHub : Hub
    {
        public async Task SendMessage(string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", message);
        }
    }

    public class TodoHub : Hub
    {
        public async Task BroadcastUpdateTodos(TodoItem Todos)
        {
            await Clients.All.SendAsync("UpdateTodos", Todos);
        }
    }

}
