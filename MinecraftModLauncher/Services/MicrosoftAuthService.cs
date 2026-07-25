using MinecraftModLauncher.Models;
using MinecraftModLauncher.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services {
    public class MicrosoftAuthService {
        // This is for the Azure Client ID
        private const string ClientId = "1055636b-c891-44b3-adb4-31067bb05af9";
        private const string Scope = "XboxLive.signin offline_access";


        private static readonly HttpClient Http = new();


        // Returns user code, URL to show the user, and the device code for polling.
        public async Task<DeviceCodeResponse> startDeviceCodeFlow() {
            var content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["client_id"] = ClientId,
                ["scope"] = Scope,
            });

            HttpResponseMessage response = await Http.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
                content);

            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Device code failed ({(int)response.StatusCode}): {json}");

            JsonElement root = JsonDocument.Parse(json).RootElement;

            return new DeviceCodeResponse {
                UserCode = root.GetProperty("user_code").GetString()!,
                VerificationUrl = root.GetProperty("verification_uri").GetString()!,
                DeviceCode = root.GetProperty("device_code").GetString()!,
                Interval = root.GetProperty("interval").GetInt32(),
                ExpiresInSeconds = root.GetProperty("expires_in").GetInt32(),
            };
        }

        // Returns mc access token and refresh token.
        public async Task<MicrosoftTokenResponse> pollForMicrosoftToken(DeviceCodeResponse deviceCode) {
            DateTime expiry = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresInSeconds);

            while (DateTime.UtcNow < expiry) {
                await Task.Delay(TimeSpan.FromSeconds(deviceCode.Interval));

                var content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode.DeviceCode,
                });

                HttpResponseMessage response = await Http.PostAsync(
                    "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                    content);

                string json = await response.Content.ReadAsStringAsync();
                JsonElement root = JsonDocument.Parse(json).RootElement;

                if (root.TryGetProperty("error", out JsonElement error)) {
                    string errorCode = error.GetString()!;

                    if (errorCode == "authorization_pending")
                        continue;

                    throw new Exception($"Authentication failed: {errorCode}");
                }

                return new MicrosoftTokenResponse {
                    AccessToken = root.GetProperty("access_token").GetString()!,
                    RefreshToken = root.GetProperty("refresh_token").GetString()!,
                    ExpiresInSeconds = root.GetProperty("expires_in").GetInt32(),
                };
            }

            throw new Exception("Device code expired. User did not authenticate in time.");
        }

        // Exchange Microsoft token for Xbox Live token
        private async Task<XboxTokenResponse> authenticateWithXboxLive(string msAccessToken) {
            var requestBody = new {
                Properties = new {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = $"d={msAccessToken}"
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            };

            string json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Http.PostAsync(
                "https://user.auth.xboxlive.com/user/authenticate", content);

            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)response.StatusCode}: {responseJson}");

            JsonElement root = JsonDocument.Parse(responseJson).RootElement;

            return new XboxTokenResponse {
                Token = root.GetProperty("Token").GetString()!,
                UserHash = root.GetProperty("DisplayClaims")
                    .GetProperty("xui")[0]
                    .GetProperty("uhs")
                    .GetString()!,
            };
        }

        // Exchange Xbox Live token for XSTS token
        private async Task<XboxTokenResponse> authenticateWithXsts(string xboxLiveToken) {
            var requestBody = new {
                Properties = new {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xboxLiveToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            };

            string json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Http.PostAsync(
                "https://xsts.auth.xboxlive.com/xsts/authorize", content);

            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
                JsonElement errorRoot = JsonDocument.Parse(responseJson).RootElement;
                if (errorRoot.TryGetProperty("XErr", out JsonElement xErr)) {
                    long errorCode = xErr.GetInt64();
                    string message = errorCode switch {
                        2148916233 => "This Microsoft account has no Xbox account. "
                                    + "Sign in to minecraft.net first to create one.",
                        2148916235 => "Xbox Live is not available in your country.",
                        2148916236 | 2148916237 => "This account needs adult verification. "
                                                + "Check Xbox settings.",
                        2148916238 => "This is a child account. A parent must add it "
                                    + "to a Family group.",
                        _ => $"XSTS authentication failed with error code {errorCode}"
                    };
                    throw new Exception(message);
                }
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"HTTP {(int)response.StatusCode}: {responseJson}");
            }

            JsonElement root = JsonDocument.Parse(responseJson).RootElement;

            return new XboxTokenResponse {
                Token = root.GetProperty("Token").GetString()!,
                UserHash = root.GetProperty("DisplayClaims")
                    .GetProperty("xui")[0]
                    .GetProperty("uhs")
                    .GetString()!,
            };
        }

        // Exchange XSTS token for mc token
        private async Task<MinecraftTokenResponse> authenticateWithMinecraft(string xstsToken, string userHash) {
            string body = JsonSerializer.Serialize(new {
                identityToken = $"XBL3.0 x={userHash};{xstsToken}"
            });

            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.minecraftservices.com/authentication/loginWithXbox");

            request.Content = new StringContent(body);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Http.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)response.StatusCode}: {responseJson}");

            JsonElement root = JsonDocument.Parse(responseJson).RootElement;

            return new MinecraftTokenResponse {
                AccessToken = root.GetProperty("access_token").GetString()!,
                ExpiresInSeconds = root.GetProperty("expires_in").GetInt32(),
            };
        }

        // Get the mc profile (username + UUID)
        private async Task<MinecraftProfile> fetchMinecraftProfile(string minecraftAccessToken) {
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                    minecraftAccessToken);

            HttpResponseMessage response = await Http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception("This Microsoft account does not own Minecraft.");

            JsonElement root = JsonDocument.Parse(json).RootElement;

            return new MinecraftProfile {
                Username = root.GetProperty("name").GetString()!,
                Uuid = root.GetProperty("id").GetString()!,
            };
        }


        // Refresh an expired token without re-authenticating
        public async Task<MicrosoftTokenResponse> refreshMicrosoftToken(string refreshToken) {
            var content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["client_id"] = ClientId,
                ["scope"] = Scope,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });

            HttpResponseMessage response = await Http.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                content);

            string json = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            JsonElement root = JsonDocument.Parse(json).RootElement;

            return new MicrosoftTokenResponse {
                AccessToken = root.GetProperty("access_token").GetString()!,
                RefreshToken = root.GetProperty("refresh_token").GetString()!,
                ExpiresInSeconds = root.GetProperty("expires_in").GetInt32(),
            };
        }

        public async Task<MinecraftAccount> authenticateFullFlow(Action<string> onStatusUpdate, Action<string, string> onUserCodeReceived) {
            onStatusUpdate("Starting Microsoft sign-in...");
            DeviceCodeResponse deviceCode = await startDeviceCodeFlow();

            onUserCodeReceived(deviceCode.UserCode, deviceCode.VerificationUrl);

            onStatusUpdate("Waiting for you to enter the code...");
            MicrosoftTokenResponse msToken = await pollForMicrosoftToken(deviceCode);
            
            onStatusUpdate("Authenticating with Xbox Live...");
            XboxTokenResponse xboxToken;
            try {
                xboxToken = await authenticateWithXboxLive(msToken.AccessToken);
            } catch (Exception ex) {
                throw new Exception($"Xbox Live failed: {ex.Message}");
            }
            
            onStatusUpdate("Authenticating with XSTS...");
            XboxTokenResponse xstsToken;
            try {
                xstsToken = await authenticateWithXsts(xboxToken.Token);
            } catch (Exception ex) {
                throw new Exception($"XSTS failed: {ex.Message}");
            }

           
            Console.WriteLine(">>> Starting Minecraft auth");
            onStatusUpdate("Authenticating with Minecraft...");
            MinecraftTokenResponse mcToken;
            try {
                mcToken = await authenticateWithMinecraft(xstsToken.Token, xstsToken.UserHash);
            } catch (Exception ex) {
                throw new Exception($"Minecraft auth failed: {ex.Message}");
            }

            Console.WriteLine(">>> Starting profile fetch");
            onStatusUpdate("Fetching profile...");
            MinecraftProfile profile;
            try {
                profile = await fetchMinecraftProfile(mcToken.AccessToken);
            } catch (Exception ex) {
                throw new Exception($"Profile fetch failed: {ex.Message}");
            }

            onStatusUpdate($"Signed in as {profile.Username}");

            return new MinecraftAccount {
                Username = profile.Username,
                Uuid = profile.Uuid,
                AccessToken = mcToken.AccessToken,
                RefreshToken = msToken.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(mcToken.ExpiresInSeconds),
            };
        }
    }


    // Data transfer objects for the auth chain
    public class DeviceCodeResponse {
        public string UserCode { get; set; } = "";
        public string VerificationUrl { get; set; } = "";
        public string DeviceCode { get; set; } = "";
        public int Interval { get; set; }
        public int ExpiresInSeconds { get; set; }
    }

    public class MicrosoftTokenResponse {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresInSeconds { get; set; }
    }

    public class XboxTokenResponse {
        public string Token { get; set; } = "";
        public string UserHash { get; set; } = "";
    }

    public class MinecraftTokenResponse {
        public string AccessToken { get; set; } = "";
        public int ExpiresInSeconds { get; set; }
    }

    public class MinecraftProfile {
        public string Username { get; set; } = "";
        public string Uuid { get; set; } = "";
    }
}
