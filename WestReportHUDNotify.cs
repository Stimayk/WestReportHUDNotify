using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WestReportSystemApiReborn;

namespace WestReportHUDNotify
{
    public class WestReportHUDNotify : BasePlugin
    {
        public override string ModuleName => "WestReportHUDNotify";
        public override string ModuleVersion => "v1.0";
        public override string ModuleAuthor => "E!N";
        public override string ModuleDescription => "Module that adds a notification to the player in the hud that a report has been sent";

        private IWestReportSystemApi? WRS_API;
        private HUDNotifyConfig? _config;
        private bool messageToHudEnabled = false;
        private string? apikey;
        private float duration;

        private readonly HttpClient _httpClient = new();

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            string configDirectory = GetConfigDirectory();
            EnsureConfigDirectory(configDirectory);
            string configPath = Path.Combine(configDirectory, "HUDNotifyConfig.json");
            _config = HUDNotifyConfig.Load(configPath);

            WRS_API = IWestReportSystemApi.Capability.Get();
            if (WRS_API == null)
            {
                Console.WriteLine($"{ModuleName} | Error: WestReportSystem API is not available.");
                return;
            }
            WRS_API.OnReportSend += GetReport;
            Console.WriteLine($"{ModuleName} | Successfully subscribed to report send events.");
            InitializeHUDNotify();
        }

        private static string GetConfigDirectory()
        {
            return Path.Combine(Server.GameDirectory, "csgo/addons/counterstrikesharp/configs/plugins/WestReportSystem/Modules");
        }

        private void EnsureConfigDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                Console.WriteLine($"{ModuleName} | Created configuration directory at: {directoryPath}");
            }
        }

        private void InitializeHUDNotify()
        {
            if (_config == null)
            {
                Console.WriteLine($"{ModuleName} | Error: Configuration is not loaded.");
                return;
            }

            apikey = _config.HUDNotifySteamWebApiKey;
            duration = _config.HUDNotifyDuration;

            if (apikey == null)
            {
                Console.WriteLine($"{ModuleName} | Initialized: ApiKey none so avatar will not be displayed, Duration = {duration}s");
                return;
            }
            Console.WriteLine($"{ModuleName} | Initialized: ApiKey detected so avatar will be displayed, Duration = {duration}s");
        }

        private async Task<string?> FetchSteamAvatarAsync(ulong steamId)
        {
            if (string.IsNullOrEmpty(apikey)) return null;

            string requestUri = $"http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apikey}&steamids={steamId}";
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(requestUri);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                JObject jsonResponse = JObject.Parse(responseBody);
                return jsonResponse["response"]?["players"]?[0]?["avatarmedium"]?.ToString();
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("Message :{0} ", e.Message);
                return null;
            }
        }

        private void GetReport(CCSPlayerController? sender, CCSPlayerController? violator, string? reason)
        {
            float notifyDuration = WRS_API?.GetConfigValue<float>("AdminNotifyDurationHUD") ?? 5.0f;

            if (violator != null)
            {
                Task.Run(async () =>
                {
                    string? avatarUrl = await FetchSteamAvatarAsync(violator.SteamID);
                    Server.NextFrame(() =>
                    {
                        RegisterListener<Listeners.OnTick>(() =>
                        {
                            if (messageToHudEnabled)
                            {
                                Utilities.GetPlayers().Where(IsValidPlayer).ToList().ForEach(p =>
                                {
                                    OnTick(sender, violator, reason, avatarUrl);
                                });
                            }
                        });
                        ToggleMessageToHud(notifyDuration, sender, violator, reason);
                    });
                });
            }
        }

        private void ToggleMessageToHud(float duration, CCSPlayerController? sender, CCSPlayerController? violator, string? reason)
        {
            messageToHudEnabled = true;
            AddTimer(duration, () => { messageToHudEnabled = false; sender = null; violator = null; reason = null; });
        }

        private void OnTick(CCSPlayerController? sender, CCSPlayerController? violator, string? reason, string? avatarUrl)
        {
            if (violator != null && sender != null && reason != null && avatarUrl != null)
            {
                sender.PrintToCenterHtml($"{WRS_API?.GetTranslatedText("wrhn.HudMessage", violator.PlayerName, reason, avatarUrl)}");
            }
        }

        private bool IsValidPlayer(CCSPlayerController player)
        {
            return player.IsValid && player.PlayerPawn?.IsValid == true && !player.IsBot && !player.IsHLTV && player.Connected == PlayerConnectedState.PlayerConnected;
        }

        public class HUDNotifyConfig
        {
            public string? HUDNotifySteamWebApiKey { get; set; }
            public float HUDNotifyDuration { get; set; } = 5.0f;

            public static HUDNotifyConfig Load(string configPath)
            {
                if (!File.Exists(configPath))
                {
                    HUDNotifyConfig defaultConfig = new();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(defaultConfig, Newtonsoft.Json.Formatting.Indented));
                    return defaultConfig;
                }

                string json = File.ReadAllText(configPath);
                return JsonConvert.DeserializeObject<HUDNotifyConfig>(json) ?? new HUDNotifyConfig();
            }
        }
    }
}