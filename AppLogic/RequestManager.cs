﻿using ChatCore;
using ChatCore.Interfaces;
using ChatCore.Services.Twitch;
using System;
using System.Text.RegularExpressions;
using Zenject;
using static Shaffuru.AppLogic.SongQueueManager;

namespace Shaffuru.AppLogic {
	class RequestManager : IInitializable {
		ChatCoreInstance chatCore;
		TwitchService twitch;
		SongQueueManager songQueueManager;

		MapPool mapPool;

		public RequestManager(SongQueueManager songQueueManager, MapPool mapPool) {
			this.songQueueManager = songQueueManager;
			this.mapPool = mapPool;
		}

		public void Initialize() {
			chatCore = ChatCoreInstance.Create();

			twitch = chatCore.RunTwitchServices();

			twitch.OnTextMessageReceived += (_, message) => Twitch_OnTextMessageReceived(message);
		}

		void Msg(string message, IChatChannel channel) {
			twitch.SendTextMessage($"! {message}", channel);
		}

		Regex diffTimePattern = new Regex("(?<diff>Easy|Normal|Hard|Expert|ExpertPlus)?( (?<timeM>[0-9]{1,2}):(?<timeS>[0-5]?[0-9])|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private void Twitch_OnTextMessageReceived(IChatMessage message) {
			if(Config.Instance.chat_request_enabled && message.Message.StartsWith("!chaos")) {
				var split = message.Message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

				if(split.Length < 2)
					return;

				var sender = message.Sender.UserName;

				if(songQueueManager.IsFull()) {
					Msg($"@{sender} The queue is full", message.Channel);
					return;
				}

				var diff = -1;
				var startTime = -1;
				string hash = null;

				// https://github.com/kinsi55/BeatSaber_SongDetails/commit/7c85cee7849794c8670ef960bc6a583ba9c68e9c 💀
				var key = split[1].ToLower();
				if(key.Length < 10) {
					try {
						hash = mapPool.GetHashFromBeatsaverId(key);
					} catch { }
				}

				if(hash == null) {
					Msg($"@{sender} Unknown Map ID", message.Channel);

				} else if(!mapPool.HasLevelId(hash)) {
					Msg($"@{sender} The Map is not downloaded or does not match the configured filters", message.Channel);

				} else if(songQueueManager.Count(x => x.source == sender) >= Config.Instance.request_limitPerUser) {
					Msg($"@{sender} You already have {Config.Instance.request_limitPerUser} Maps in the queue", message.Channel);

				} else if(songQueueManager.Contains(x => MapPool.GetHashOfLevelid(x.levelId) == hash)) {
					Msg($"@{sender} This song is already in the queue currently", message.Channel);

				} else {
					var theMappe = mapPool.filteredLevels[mapPool.requestableLevels[hash]];

					if(split.Length > 2 && (Config.Instance.request_allowSpecificDiff || Config.Instance.request_allowSpecificTime)) {
						var m = diffTimePattern.Match(message.Message);

						if(split.Length >= 4 && !m.Groups["timeM"].Success) {
							Msg($"@{sender} Invalid time (Ex: 2:33)", message.Channel);
							return;
						} else if(!m.Groups["diff"].Success) {
							Msg($"@{sender} Invalid difficulty (Ex: 'hard' or 'ExpertPlus')", message.Channel);
							return;
						}

						if(
							Config.Instance.request_allowSpecificDiff &&
							m.Groups["diff"].Success &&
							Enum.TryParse<BeatmapDifficulty>(m.Groups["diff"].Value, true, out var requestedDiff)
						) {
							if(!theMappe.IsDiffValid(requestedDiff)) {
								Msg($"@{sender} The {requestedDiff} difficulty does not match the configured filters", message.Channel);
								return;
							}

							diff = (int)requestedDiff;
						}

						if(
							Config.Instance.request_allowSpecificTime &&
							m.Groups["timeM"].Success &&
							m.Groups["timeS"].Success &&
							int.TryParse(m.Groups["timeM"].Value, out var timeM) &&
							int.TryParse(m.Groups["timeS"].Value, out var timeS)
						) {
							startTime = timeS + (timeM * 60);
						}
					}

					if(diff == -1)
						diff = (int)theMappe.GetRandomValidDiff();

					var queued = songQueueManager.EnqueueSong(new QueuedSong(
						$"custom_level_{hash}",
						diff,
						startTime,
						-1,
						sender
					));

					if(queued) {
						Msg($"@{sender} Queued {split[1]} ({(BeatmapDifficulty)diff})", message.Channel);
					} else {
						Msg($"@{sender} Couldnt queue map (Unknown error)", message.Channel);
					}
				}
			}
		}
	}
}
