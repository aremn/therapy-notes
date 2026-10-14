using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;
using Postgrest.Attributes;
using Postgrest.Models;
using Supabase.Gotrue;

namespace AudioRecorderTrayApp.Services
{
    [Table("clients")]
    public class RemoteClient : BaseModel
    {
        [PrimaryKey("id")]
        public Guid Id { get; set; }

        [Column("creator_id")]
        public Guid CreatorId { get; set; }
    }

    public static class ClientStore
    {
        public static event Action? ClientsChanged;
        public static async Task<IReadOnlyList<AudioRecorderTrayApp.Models.Client>> LoadAllAsync()
        {
            await DatabaseService.InitializeAsync();
            var localClients = await DatabaseService.Db.Table<AudioRecorderTrayApp.Models.Client>().Where(c => c.IsActive && !c.IsDeleted).OrderBy(c => c.Name).ToListAsync();
            return localClients;
        }

        public static async Task<AudioRecorderTrayApp.Models.Client?> GetByIdAsync(string id)
        {
            await DatabaseService.InitializeAsync();
            return await DatabaseService.Db.Table<AudioRecorderTrayApp.Models.Client>().FirstOrDefaultAsync(c => c.Id == id);
        }

        public static async Task AddOrUpdateAsync(AudioRecorderTrayApp.Models.Client client)
        {
            await DatabaseService.InitializeAsync();
            if (string.IsNullOrEmpty(client.RemoteId))
            {
                var newRemoteClient = await CreateRemoteClientAsync();
                if (newRemoteClient != null)
                {
                    client.RemoteId = newRemoteClient.Id.ToString();
                    await DatabaseService.Db.InsertAsync(client);
                }
                else
                {
                    Logger.Error("Failed to create remote client. Local client was not saved.");
                    return;
                }
            }
            else
            {
                await DatabaseService.Db.UpdateAsync(client);
            }

            ClientsChanged?.Invoke();
        }

        public static async Task RemoveAsync(string id)
        {
            await DatabaseService.InitializeAsync();
            var client = await GetByIdAsync(id);
            if (client != null)
            {
                client.IsActive = false;
                client.IsDeleted = true;
                await DatabaseService.Db.UpdateAsync(client);
                ClientsChanged?.Invoke();
            }
        }

        private static async Task<RemoteClient?> CreateRemoteClientAsync()
        {
            try
            {
                var user = App.Supabase.Auth.CurrentUser;
                if (user == null)
                    throw new Exception("User not authenticated.");
                var remoteClient = new RemoteClient
                {
                    CreatorId = Guid.Parse(user.Id!)
                };
                var response = await App.Supabase.From<RemoteClient>().Insert(remoteClient);
                var createdClient = response.Models.FirstOrDefault();
                if (createdClient != null)
                {
                    Logger.Info($"Successfully created remote client with ID: {createdClient.Id}");
                }

                return createdClient;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating remote client in Supabase: {ex.Message}");
                return null;
            }
        }
    }
}
