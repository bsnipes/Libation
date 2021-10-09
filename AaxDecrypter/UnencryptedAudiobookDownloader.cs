﻿using Dinah.Core.IO;
using Dinah.Core.Net.Http;
using Dinah.Core.StepRunner;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace AaxDecrypter
{
	public class UnencryptedAudiobookDownloader : AudiobookDownloadBase
	{
		protected override StepSequence Steps { get; }

		public UnencryptedAudiobookDownloader(string outFileName, string cacheDirectory, DownloadLicense dlLic)
			: base(outFileName, cacheDirectory, dlLic)
		{
			Steps = new StepSequence
			{
				Name = "Download Mp3 Audiobook",

				["Step 1: Get Mp3 Metadata"] = Step1_GetMetadata,
				["Step 2: Download Audiobook"] = Step2_DownloadAudiobookAsSingleFile,
				["Step 3: Create Cue"] = Step3_CreateCue,
				["Step 4: Cleanup"] = Step4_Cleanup,
			};
		}

		public override void Cancel()
		{
			IsCanceled = true;
			CloseInputFileStream();
		}

		protected override bool Step1_GetMetadata()
		{
			OnRetrievedCoverArt(null);

			return !IsCanceled;
		}

		protected override bool Step2_DownloadAudiobookAsSingleFile()
		{
			DateTime startTime = DateTime.Now;

			//MUST put InputFileStream.Length first, because it starts background downloader.

			while (InputFileStream.Length > InputFileStream.WritePosition && !InputFileStream.IsCancelled) 
			{
				var rate = InputFileStream.WritePosition / (DateTime.Now - startTime).TotalSeconds;

				var estTimeRemaining = (InputFileStream.Length - InputFileStream.WritePosition) / rate;

				if (double.IsNormal(estTimeRemaining))
					OnDecryptTimeRemaining(TimeSpan.FromSeconds(estTimeRemaining));

				var progressPercent = (double)InputFileStream.WritePosition / InputFileStream.Length;

				OnDecryptProgressUpdate(
					new DownloadProgress
					{
						ProgressPercentage = 100 * progressPercent,
						BytesReceived = (long)(InputFileStream.Length * progressPercent),
						TotalBytesToReceive = InputFileStream.Length
					});
				Thread.Sleep(200);
			}

			CloseInputFileStream();

			if (File.Exists(OutputFileName))
				FileExt.SafeDelete(OutputFileName);

			FileExt.SafeMove(InputFileStream.SaveFilePath, OutputFileName);
			OnFileCreated(OutputFileName);

			return !IsCanceled;
		}
	}
}