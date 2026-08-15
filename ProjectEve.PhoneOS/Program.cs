using ProjectEve.PhoneOS.Components;
using ProjectEve.PhoneOS.Components.Pages;

namespace ProjectEve.PhoneOS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Optional LAN/server binding for phone/tablet clients.
            var phoneOsUrls = Environment.GetEnvironmentVariable("EVE_PHONEOS_URLS");

            if (!string.IsNullOrWhiteSpace(phoneOsUrls))
                builder.WebHost.UseUrls(phoneOsUrls);


            // ---------- Razor ----------
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();


            // ---------- PhoneOS services ----------
            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.PhoneSessionService>();
            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.PlayerProfileService>();
            builder.Services.AddScoped<ProjectEve.PhoneOS.Services.SceneUiStateService>();
            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.PhoneThreadPresenceService>();

            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.GameTimeTextCommandParser>();
            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.GameplayTimeControllerService>();


            // ---------- World Time ----------
            // ProjectEve owns world time.
            builder.Services.AddSingleton<ProjectEve.Core.Time.IGamePacingService,
                ProjectEve.Time.GamePacingService>();

            builder.Services.AddSingleton<ProjectEve.Core.Time.IGameTimeService,
                ProjectEve.Time.ProjectEveGameTimeService>();

            // REQUIRED by GameplayTimeControllerService
            builder.Services.AddSingleton<ProjectEve.Core.Time.IWorldAdvanceCoordinator,
                ProjectEve.Time.WorldAdvanceCoordinator>();

            // ---------- World / Travel ----------
            builder.Services.AddSingleton<ProjectEve.Core.World.IKnownLocationService,
                ProjectEve.World.KnownLocationService>();

            builder.Services.AddSingleton<ProjectEve.Core.World.IWorldOccupancyService,
                ProjectEve.World.WorldOccupancyService>();

            builder.Services.AddSingleton<ProjectEve.Core.World.IPlayerWorldPresenceService,
                ProjectEve.World.PlayerWorldPresenceService>();

            builder.Services.AddSingleton<ProjectEve.Core.World.IPlayerTravelService,
                ProjectEve.World.PlayerTravelService>();

            // ---------- Scene ----------
            builder.Services.AddSingleton<ProjectEve.Core.Scene.IScenePerceptionService,
                ProjectEve.Scene.ScenePerceptionService>();

            builder.Services.AddSingleton<ProjectEve.Core.Scene.ISharedScenePresenceCoordinator,
                ProjectEve.Scene.SharedScenePresenceCoordinator>();

            builder.Services.AddSingleton<ProjectEve.Core.Scene.ISceneSpatialInteractionService,
                ProjectEve.Scene.SceneSpatialInteractionService>();

            builder.Services.AddSingleton<ProjectEve.Core.Scene.IGroupSceneConversationOrchestrator,
                ProjectEve.Scene.GroupSceneConversationOrchestrator>();


            // ---------- NPC Knowledge ----------
            builder.Services.AddSingleton<ProjectEve.Core.Knowledge.INpcKnowledgeService,
                ProjectEve.Knowledge.NpcKnowledgeService>();

            builder.Services.AddSingleton<ProjectEve.Core.Knowledge.INpcKnowledgeCommunicationService,
                ProjectEve.Knowledge.NpcKnowledgeCommunicationService>();


            // ---------- Social / Gossip ----------
            builder.Services.AddSingleton<ProjectEve.Core.Knowledge.INpcSocialDecisionService,
                ProjectEve.Knowledge.NpcSocialDecisionService>();


            // ---------- Conversation ----------
            builder.Services.AddSingleton<ProjectEve.Core.Chat.IConversationChatService,
                ProjectEve.Chat.ProjectEveConversationService>();

            builder.Services.AddSingleton<ProjectEve.Core.Chat.IEveChatService,
                ProjectEve.Chat.BrainEveChatService>();

            builder.Services.AddSingleton<ProjectEve.Core.Chat.SceneNarrationService>();


            // ---------- Phone Messaging ----------
            builder.Services.AddSingleton<ProjectEve.Core.Phone.IPhoneResponseScheduler,
                ProjectEve.Phone.NpcPhoneResponseScheduler>();

            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.PhoneMessagingService>();

            builder.Services.AddHostedService<ProjectEve.PhoneOS.Services.PhoneMessagingService>(
                sp => sp.GetRequiredService<ProjectEve.PhoneOS.Services.PhoneMessagingService>());


            // ---------- TTS ----------
            builder.Services.AddSingleton<ProjectEve.Core.Chat.TtsBakeService>();


            // ---------- Phase 16 NPC Initiated Communication ----------
            builder.Services.AddSingleton<ProjectEve.Core.Phone.INpcInitiatedContactService,
                ProjectEve.Phone.NpcInitiatedContactService>();

            builder.Services.AddSingleton<ProjectEve.PhoneOS.Services.NpcInitiatedPhoneDeliveryService>();

            builder.Services.AddHostedService<ProjectEve.PhoneOS.Services.NpcInitiatedContactHostedService>();


            var app = builder.Build();


            // ---------- Middleware ----------
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }


            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAntiforgery();


            app.MapStaticAssets();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();



            // ---------- Voice API ----------
            app.MapGet("/api/voice/{fileName}", (string fileName) =>
            {
                if (string.IsNullOrWhiteSpace(fileName) ||
                    fileName.IndexOfAny(
                        System.IO.Path.GetInvalidFileNameChars()) >= 0)
                {
                    return Results.BadRequest();
                }


                var dir = @"D:\ProjectEve\EveData\voice";

                Directory.CreateDirectory(dir);


                var path = Path.Combine(dir, fileName);


                if (!File.Exists(path))
                    return Results.NotFound($"Missing: {path}");


                return Results.File(path, "audio/wav");
            });



            // ---------- Database Seed ----------
            try
            {
                ProjectEve.Core.Database.LocationDb.SeedDefaults();
            }
            catch
            {
            }



            // ---------- Start TTS ----------
            try
            {
                var tts =
                    app.Services.GetRequiredService<ProjectEve.Core.Chat.TtsBakeService>();

                tts.Start();

                Console.WriteLine(
                    "TTS worker started (Qwen Eve2/Narrative).");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "TTS start skipped: " + ex.Message);
            }


            app.Run();
        }
    }
}