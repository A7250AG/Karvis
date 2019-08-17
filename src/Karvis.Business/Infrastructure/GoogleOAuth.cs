using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using Karvis.Business.Configuration;
using Npgsql;

namespace Karvis.Business.Infrastructure
{
    public class GoogleOAuth
    {
        private KarvisConfiguration KarvisConfiguration { get; }
        public GoogleOAuth(KarvisConfiguration configuration)
        {
            KarvisConfiguration = configuration;
        }

        public string GetTokenForUser(ulong userId)
        {
            using (var connection = new NpgsqlConnection(KarvisConfiguration.InfrastructureConfiguration.KarvisConnectionString))
            {
                connection.Open();
                var value = connection.Query<string>($"Select token from google_oauth where discord_userid = {userId};").FirstOrDefault();
                return value;
            }
        }

        public string GetRefreshTokenForUser(ulong userId)
        {
            using (var connection = new NpgsqlConnection(KarvisConfiguration.InfrastructureConfiguration.KarvisConnectionString))
            {
                connection.Open();
                var value = connection.Query<string>($"Select refreshToken from google_oauth where discord_userid = {userId};").FirstOrDefault();
                return value;
            }
        }

        public void StoreCredentialsForUser(string googleUserid, string token, string refreshToken, ulong discordUserid)
        {
            using (var connection = new NpgsqlConnection(KarvisConfiguration.InfrastructureConfiguration.KarvisConnectionString))
            {
                connection.Open();
                var existing = connection.Query<string>($"Select userId from google_oauth where discord_userid={discordUserid};").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(existing))
                {
                    connection.Execute($"Insert into google_oauth (google_userid, token, refreshToken, discord_userid) values ('{googleUserid}', '{token}', '{refreshToken}', {discordUserid});");
                }
                else
                {
                    connection.Execute($"update google_oauth set token='{token}', refreshToken='{refreshToken}' where discord_userid = {discordUserid};");
                }
            }
        }
    }
}
