﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using Microsoft.Bot.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Microsoft.Bot.Builder.Core.Extensions.Tests
{
    /// <summary>
    /// Helpers to get activities from trancript files
    /// </summary>
    public static class TranscriptUtilities
    {
        /// <summary>
        /// Loads a list of activities from a transcript file.
        /// Use the context of the test to find the transcript file
        /// </summary>
        /// <param name="context">Test context</param>
        /// <returns>A list of activities to test</returns>
        public static IEnumerable<IActivity> GetFromTestContext(TestContext context)
        {
            var relativePath = Path.Combine(context.FullyQualifiedTestClassName.Split('.').Last(), $"{context.TestName}.chat");
            return GetActivities(relativePath);
        }

        public static IEnumerable<IActivity> GetActivities(string relativePath)
        {
            var transcriptsRootFolder = TranscriptUtilities.EnsureTranscriptsDownload();
            var path = Path.Combine(transcriptsRootFolder, relativePath);
            if (!File.Exists(path))
            {
                path = Path.Combine(transcriptsRootFolder, relativePath.Replace(".chat", ".transcript", StringComparison.InvariantCultureIgnoreCase));
            }
            if (!File.Exists(path))
            {
                Assert.Fail($"Required transcript file '{path}' does not exists in '{transcriptsRootFolder}' folder. Review the 'TranscriptsRootFolder' environment variable value.");
            }

            string content;
            if (string.Equals(path.Split('.').Last(), "chat", StringComparison.InvariantCultureIgnoreCase))
            {
                content = Chatdown(path);
            }
            else
            {
                content = File.ReadAllText(path);
            }

            var activities = JsonConvert.DeserializeObject<List<Activity>>(content);

            var lastActivity = activities.Last();
            if (lastActivity.Text.Last() == '\n')
            {
                lastActivity.Text = lastActivity.Text.Remove(lastActivity.Text.Length - 1);
            }

            return activities.Take(activities.Count - 1).Append(lastActivity);
        }

        private static readonly object syncRoot = new object();
        private static string TranscriptsTemporalPath { get; set; }

        public static string EnsureTranscriptsDownload()
        {
            if (!string.IsNullOrWhiteSpace(TranscriptsTemporalPath))
            {
                return TranscriptsTemporalPath;
            }

            var transcriptsZipUrl = TestUtilities.GetKey("BOTBUILDER_TRANSCRIPTS_LOCATION") ?? "https://github.com/southworkscom/BotBuilder/archive/botbuilder-v4-transcripts.zip";
            const string transcriptsZipFolder = "/Common/Transcripts/"; // Folder within the repo/zip

            var tempPath = Path.GetTempPath();
            var zipFilePath = Path.Combine(tempPath, Path.GetFileName(transcriptsZipUrl));

            lock (syncRoot)
            {
                if (!string.IsNullOrWhiteSpace(TranscriptsTemporalPath))
                {
                    return TranscriptsTemporalPath;
                }

                // Only download and extract zip when provided a valid absolute url. Otherwise, use it as local path
                if (Uri.IsWellFormedUriString(transcriptsZipUrl, UriKind.Absolute))
                {
                    DownloadFile(transcriptsZipUrl, zipFilePath);

                    var transcriptsExtractionPath = Path.Combine(tempPath, "Transcripts/");
                    ExtractZipFolder(zipFilePath, transcriptsZipFolder, transcriptsExtractionPath);

                    // Set TranscriptsTemporalPath for next use
                    TranscriptsTemporalPath = transcriptsExtractionPath;
                }
                else
                {
                    TranscriptsTemporalPath = transcriptsZipUrl;
                }

                return TranscriptsTemporalPath;
            }
        }

        private static void ExtractZipFolder(string zipFilePath, string zipFolder, string path)
        {
            using (var zipArchive = ZipFile.OpenRead(zipFilePath))
            {
                var zipFolderEntry = zipArchive.Entries.SingleOrDefault(e => e.FullName.EndsWith(zipFolder));
                if (zipFolderEntry == null)
                {
                    throw new InvalidOperationException($"Folder '{zipFolder}' not found in '{zipFilePath}' file.");
                }

                // Create extraction folder in temp folder
                CreateDirectoryIfNotExists(path);

                // Iterate each entry in the zip file
                foreach (var entry in zipArchive.Entries
                    .Where(e => e.FullName.StartsWith(zipFolderEntry.FullName)))
                {
                    var entryName = entry.FullName.Remove(0, zipFolderEntry.FullName.Length);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // No Name, it is a folder
                        CreateDirectoryIfNotExists(Path.Combine(path, entryName));
                    }
                    else
                    {
                        entry.ExtractToFile(Path.Combine(path, entryName), overwrite: true);
                    }
                }
            }
        }

        private static void DownloadFile(string url, string path)
        {
            // Download file from url to disk
            using (var httpClient = new HttpClient())
            using (var urlStream = httpClient.GetStreamAsync(url).Result)
            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                urlStream.CopyTo(fileStream);
            }
        }

        private static void CreateDirectoryIfNotExists(string tempTranscriptPath)
        {
            if (!Directory.Exists(tempTranscriptPath))
            {
                Directory.CreateDirectory(tempTranscriptPath);
            }
        }

        public static string Chatdown(string path)
        {
            var file = new FileInfo(path);
            var chatdown = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chatdown_gen.cmd",
                Arguments = file.FullName,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            var chatdownProcess = System.Diagnostics.Process.Start(chatdown);
            var content = chatdownProcess.StandardOutput.ReadToEnd();
            chatdownProcess.WaitForExit();
            if (string.IsNullOrEmpty(content))
            {
                throw new Exception("Chatdown error. Please check if chatdown is correctly installed or install it with \"npm i -g chatdown\"");
            }
            return content;
        }
        
        /// <summary>
        /// Get a conversation reference.
        /// This method can be used to set the conversation reference needed to create a <see cref="Adapters.TestAdapter"/>
        /// </summary>
        /// <param name="activity"></param>
        /// <returns>A valid conversation reference to the activity provides</returns>
        public static ConversationReference GetConversationReference(this IActivity activity)
        {
            bool IsReply(IActivity act) => string.Equals("bot", act.From?.Role, StringComparison.InvariantCultureIgnoreCase);
            var bot = IsReply(activity) ? activity.From : activity.Recipient;
            var user = IsReply(activity) ? activity.Recipient : activity.From;
            return new ConversationReference
            {
                User = user,
                Bot = bot,
                Conversation = activity.Conversation,
                ChannelId = activity.ChannelId,
                ServiceUrl = activity.ServiceUrl
            };
        }
    }
}
