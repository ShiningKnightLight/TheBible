﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Resources.Core;
using Windows.ApplicationModel.VoiceCommands;

namespace TheBible.VoiceCommands
{
    /// <summary>
    /// This command service is the entry point of background processing
    /// voice commands that integrate into Cortana. You'll define it in
    /// TheBible project under the app manifest. Note: Take a look at the
    /// UWP Cortana Voice Command sample for The Bible to see the
    /// original implementation.
    /// </summary>
    public sealed class TheBibleVoiceCommandService : IBackgroundTask
    {
        /// <summary>
        /// Cortana Related:
        /// </summary>
        VoiceCommandServiceConnection voiceServiceConnection;

        /// <summary>
        /// Cortana Related:
        /// </summary>
        BackgroundTaskDeferral backgroundDeferral;

        /// <summary>
        /// ResourceMap containing localized strings for display in Cortana.
        /// </summary>
        ResourceMap cortanaResourceMap;

        /// <summary>
        /// The context for localized strings.
        /// </summary>
        ResourceContext cortanaContext;

        /// <summary>
        /// Get globalization-aware date formats.
        /// </summary>
        DateTimeFormatInfo dateFormatInfo;

        /// <summary>
        /// Background task entrypoint. Voice Commands using the <VoiceCommandService Target="...">
        /// tag will invoke this when they are recognized by Cortana, passing along details of the 
        /// invocation. 
        /// 
        /// Background tasks must respond to activation by Cortana within 0.5 seconds, and must 
        /// report progress to Cortana every 5 seconds (unless Cortana is waiting for user
        /// input). There is no execution time limit on the background task managed by Cortana,
        /// but developers should use plmdebug (https://msdn.microsoft.com/en-us/library/windows/hardware/jj680085%28v=vs.85%29.aspx)
        /// on the Cortana app package in order to prevent Cortana timing out the task during
        /// debugging.
        /// 
        /// Cortana dismisses its UI if it loses focus. This will cause it to terminate the background
        /// task, even if the background task is being debugged. Use of Remote Debugging is recommended
        /// in order to debug background task behaviors. In order to debug background tasks, open the
        /// project properties for the app package (not the background task project), and enable
        /// Debug -> "Do not launch, but debug my code when it starts". Alternatively, add a long
        /// initial progress screen, and attach to the background task process while it executes.
        /// </summary>
        /// <param name="taskInstance">Connection to the hosting background service process.</param>
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            backgroundDeferral = taskInstance.GetDeferral();

            // Register to receive an event if Cortana dismisses the background task. This will
            // occur if the task takes too long to respond, or if Cortana's UI is dismissed.
            // Any pending operations should be cancelled or waited on to clean up where possible.
            taskInstance.Canceled += OnTaskCanceled;

            var triggerDetails = taskInstance.TriggerDetails as AppServiceTriggerDetails;

            // Load localized resources for strings sent to Cortana to be displayed to the user.
            cortanaResourceMap = ResourceManager.Current.MainResourceMap.GetSubtree("Resources");

            // Select the system language, which is what Cortana should be running as.
            cortanaContext = ResourceContext.GetForViewIndependentUse();

            // Get the currently used system date format
            dateFormatInfo = CultureInfo.CurrentCulture.DateTimeFormat;

            // This should match the uap:AppService and VoiceCommandService references from the 
            // package manifest and VCD files, respectively. Make sure we've been launched by
            // a Cortana Voice Command.
            if (triggerDetails != null && triggerDetails.Name == "TheBibleVoiceCommandService")
            {
                try
                {
                    voiceServiceConnection =
                        VoiceCommandServiceConnection.FromAppServiceTriggerDetails(
                            triggerDetails);

                    voiceServiceConnection.VoiceCommandCompleted += OnVoiceCommandCompleted;

                    // GetVoiceCommandAsync establishes initial connection to Cortana, and must be called prior to any 
                    // messages sent to Cortana. Attempting to use ReportSuccessAsync, ReportProgressAsync, etc
                    // prior to calling this will produce undefined behavior.
                    VoiceCommand voiceCommand = await voiceServiceConnection.GetVoiceCommandAsync();

                    // Depending on the operation (defined in TheBible:TheBibleCommands.xml)
                    // perform the appropriate command.
                    switch (voiceCommand.CommandName)
                    {
                        case "openBible":
                            LaunchAppInForeground();
                            //var destination = voiceCommand.Properties["books"][0];
                            //await SendCompletionMessageForDestination(destination);
                            break;
                        case "thankYouBible":
                            var userMessage = new VoiceCommandUserMessage();
                            var message = "Why thank you! I'm glad that you appreciate all the hard effort and sacrifices I've made to bring you the Living Word of God.";

                            userMessage.DisplayMessage = message;
                            userMessage.SpokenMessage = message;
                            var response = VoiceCommandResponse.CreateResponse(userMessage);
                            await voiceServiceConnection.ReportSuccessAsync(response);

                            break;
                        default:
                            // As with app activation VCDs, we need to handle the possibility that
                            // an app update may remove a voice command that is still registered.
                            // This can happen if the user hasn't run an app since an update.
                            LaunchAppInForeground();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Handling Voice Command failed " + ex.ToString());
                }
            }
        }

        /// <summary>
        /// Provide a simple response that launches the app. Expected to be used in the
        /// case where the voice command could not be recognized (eg, a VCD/code mismatch.)
        /// </summary>
        private async void LaunchAppInForeground()
        {
            var userMessage = new VoiceCommandUserMessage();
            userMessage.SpokenMessage = cortanaResourceMap.GetValue("LaunchingTheBible", cortanaContext).ValueAsString;

            var response = VoiceCommandResponse.CreateResponse(userMessage);

            response.AppLaunchArgument = "";

            await voiceServiceConnection.RequestAppLaunchAsync(response);
        }

        /// <summary>
        /// Handle the completion of the voice command. Your app may be cancelled
        /// for a variety of reasons, such as user cancellation or not providing 
        /// progress to Cortana in a timely fashion. Clean up any pending long-running
        /// operations (eg, network requests).
        /// </summary>
        /// <param name="sender">The voice connection associated with the command.</param>
        /// <param name="args">Contains an Enumeration indicating why the command was terminated.</param>
        private void OnVoiceCommandCompleted(VoiceCommandServiceConnection sender, VoiceCommandCompletedEventArgs args)
        {
            if (this.backgroundDeferral != null)
            {
                this.backgroundDeferral.Complete();
            }
        }

        /// <summary>
        /// When the background task is cancelled, clean up/cancel any ongoing long-running operations.
        /// This cancellation notice may not be due to Cortana directly. The voice command connection will
        /// typically already be destroyed by this point and should not be expected to be active.
        /// </summary>
        /// <param name="sender">This background task instance</param>
        /// <param name="reason">Contains an enumeration with the reason for task cancellation</param>
        private void OnTaskCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            System.Diagnostics.Debug.WriteLine("Task cancelled, clean up");
            if (this.backgroundDeferral != null)
            {
                //Complete the service deferral
                this.backgroundDeferral.Complete();
            }
        }

        /// <summary>
        /// Show a progress screen. These should be posted at least every 5 seconds for a 
        /// long-running operation, such as accessing network resources over a mobile 
        /// carrier network.
        /// </summary>
        /// <param name="message">The message to display, relating to the task being performed.</param>
        /// <returns></returns>
        private async Task ShowProgressScreen(string message)
        {
            var userProgressMessage = new VoiceCommandUserMessage();
            userProgressMessage.DisplayMessage = userProgressMessage.SpokenMessage = message;

            VoiceCommandResponse response = VoiceCommandResponse.CreateResponse(userProgressMessage);
            await voiceServiceConnection.ReportProgressAsync(response);
        }

        /// <summary>
        /// Search for, and show details related to a single trip, if the trip can be
        /// found. This demonstrates a simple response flow in Cortana.
        /// </summary>
        /// <param name="destination">The destination, expected to be in the phrase list.</param>
        /// <returns></returns>
        private async Task SendCompletionMessageForDestination(string destination)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string loadingTripToDestination = string.Format(
                       cortanaResourceMap.GetValue("OpeningTheBibleToChapter", cortanaContext).ValueAsString,
                       destination);

            await ShowProgressScreen(loadingTripToDestination);
            //TheBible.Model.TripStore store = new Model.TripStore();
            //await store.LoadTrips();

            //// Look for the specified trip. The destination *should* be pulled from the grammar we
            //// provided, and the subsequently updated phrase list, so it should be a 1:1 match, including case.
            //// However, we might have multiple trips to the destination. For now, we just pick the first.
            //IEnumerable<Model.Trip> trips = store.Trips.Where(p => p.Destination == destination);

            var userMessage = new VoiceCommandUserMessage();
            var destinationsContentTiles = new List<VoiceCommandContentTile>();
            //if (trips.Count() == 0)
            //{
            //    // In this scenario, perhaps someone has modified data on your service outside of your 
            //    // control. If you're accessing a remote service, having a background task that
            //    // periodically refreshes the phrase list so it's likely to be in sync is ideal.
            //    // This is unlikely to occur for this sample app, however.
            //    string foundNoTripToDestination = string.Format(
            //           cortanaResourceMap.GetValue("FoundNoTripToDestination", cortanaContext).ValueAsString,
            //           destination);
            //    userMessage.DisplayMessage = foundNoTripToDestination;
            //    userMessage.SpokenMessage = foundNoTripToDestination;
            //}
            //else
            //{
            //    // Set a title message for the page.
            //    string message = "";
            //    if (trips.Count() > 1)
            //    {
            //        message = cortanaResourceMap.GetValue("PluralUpcomingTrips", cortanaContext).ValueAsString;
            //    }
            //    else
            //    {
            //        message = cortanaResourceMap.GetValue("SingularUpcomingTrip", cortanaContext).ValueAsString;
            //    }
            //    userMessage.DisplayMessage = message;
            //    userMessage.SpokenMessage = message;

            //    // file in tiles for each destination, to display information about the trips without
            //    // launching the app.
            //    foreach (Model.Trip trip in trips)
            //    {
            //        int i = 1;

            //        var destinationTile = new VoiceCommandContentTile();

            //        // To handle UI scaling, Cortana automatically looks up files with FileName.scale-<n>.ext formats based on the requested filename.
            //        // See the VoiceCommandService\Images folder for an example.
            //        destinationTile.ContentTileType = VoiceCommandContentTileType.TitleWith68x68IconAndText;
            //        destinationTile.Image = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///AdventureWorks.VoiceCommands/Images/GreyTile.png"));

            //        destinationTile.AppLaunchArgument = trip.Destination;
            //        destinationTile.Title = trip.Destination;
            //        if (trip.StartDate != null)
            //        {
            //            destinationTile.TextLine1 = trip.StartDate.Value.ToString(dateFormatInfo.LongDatePattern);
            //        }
            //        else
            //        {
            //            destinationTile.TextLine1 = trip.Destination + " " + i;
            //        }

            //        destinationsContentTiles.Add(destinationTile);
            //        i++;
            //    }
            //}

            var response = VoiceCommandResponse.CreateResponse(userMessage, destinationsContentTiles);

            //if (trips.Count() > 0)
            //{
            //    response.AppLaunchArgument = destination;
            //}

            await voiceServiceConnection.ReportSuccessAsync(response);
        }

    }
}
