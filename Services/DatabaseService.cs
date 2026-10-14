using System;
using System.IO;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;
using SQLite;

namespace AudioRecorderTrayApp.Services
{
    public static class DatabaseService
    {
        private static SQLiteAsyncConnection? _db;
        private static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "local.db");
        public static SQLiteAsyncConnection Db => _db ?? throw new InvalidOperationException("Database not initialized");

        public static async Task InitializeAsync()
        {
            if (_db != null)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            _db = new SQLiteAsyncConnection(DbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.CreateTableAsync<Client>();
        }
    }
}
