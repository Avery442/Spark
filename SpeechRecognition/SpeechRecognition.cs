﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Vosk;
using NAudio.Wave;
using Newtonsoft.Json;

namespace Spark
{
	public class SpeechRecognition
	{
		private bool capturing;

		private WaveIn micCapture;
		private WasapiLoopbackCapture speakerCapture;
		public float micLevel = 0;
		public float speakerLevel = 0;
		private VoskRecognizer voskRecMic;
		private VoskRecognizer voskRecSpeaker;

		public bool Enabled
		{
			get => capturing;
			set
			{
				if (value != capturing)
				{
					try
					{
						if (value)
						{
							// speechRecognizer.ContinuousRecognitionSession.StartAsync();
						}
						else
						{
							// speechRecognizer.ContinuousRecognitionSession.StopAsync();
						}
					}
					catch (Exception e)
					{
						Logger.LogRow(Logger.LogType.Error, "Error starting/stopping voice rec.\n" + e);
					}
				}

				capturing = value;
			}
		}

		public SpeechRecognition()
		{
			try
			{
				Vosk.Vosk.SetLogLevel(0);
				Model model = new Model("SpeechRecognition/vosk-model-small-en-us-0.15");
				
				voskRecMic = new VoskRecognizer(model, 16000f);
				voskRecMic.SetMaxAlternatives(10);
				voskRecMic.SetWords(true);

				micCapture = new WaveIn();
				micCapture.WaveFormat = new WaveFormat(16000, 1);
				micCapture.DeviceNumber = GetMicByName(SparkSettings.instance.microphone);
				micCapture.DataAvailable += MicDataAvailable;
				micCapture.StartRecording();
				
				// voskRecSpeaker = new VoskRecognizer(model, 16000f);
				// voskRecSpeaker.SetMaxAlternatives(10);
				// voskRecSpeaker.SetWords(true);

				// speakerCapture = new WasapiLoopbackCapture(GetSpeakerByName(SparkSettings.instance.speaker));
				// speakerCapture.DataAvailable += SpeakerDataAvailable;
				// speakerCapture.StartRecording();
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, "Error starting voice rec.\n" + e);
			}
		}

		private void SpeakerDataAvailable(object sender, WaveInEventArgs e)
		{
			speakerLevel = 0;

			float[] floats = new float[e.BytesRecorded/4];

			MemoryStream mem = new MemoryStream(e.Buffer);
			BinaryReader reader = new BinaryReader(mem);
			for (int index = 0; index < e.BytesRecorded / 4; index++)
			{
				float sample = reader.ReadSingle();

				// absolute value 
				if (sample < 0) sample = -sample;
				// is this the max value?
				if (sample > speakerLevel) speakerLevel = sample;

				floats[index/4] = sample;
			}

			if (voskRecSpeaker.AcceptWaveform(floats, floats.Length))
			{
				HandleResult(voskRecSpeaker.Result());
			}
		}

		private void MicDataAvailable(object sender, WaveInEventArgs e)
		{
			micLevel = 0;
			// interpret as 16 bit audio
			for (int index = 0; index < e.BytesRecorded; index += 2)
			{
				short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index + 0]);
				// to floating point
				float sample32 = sample / 32768f;
				// absolute value 
				if (sample32 < 0) sample32 = -sample32;
				// is this the max value?
				if (sample32 > micLevel) micLevel = sample32;
			}

			if (voskRecMic.AcceptWaveform(e.Buffer, e.BytesRecorded))
			{
				HandleResult(voskRecMic.Result());
			}
		}

		private static void HandleResult(string result)
		{
			try
			{
				string[] clipTerms =
				{
					"clip that",
					"quebec",
					"hope that",
					"could that",
					"cop that"
				};

				Dictionary<string, List<Dictionary<string, object>>> r = JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, object>>>>(result);
				if (r == null) return;
				foreach (Dictionary<string, object> alt in r["alternatives"])
				{
					if (string.IsNullOrWhiteSpace(alt["text"].ToString())) continue;
					
					Debug.WriteLine(alt["text"].ToString());
					
					foreach (string clipTerm in clipTerms)
					{
						if (alt["text"].ToString()?.Contains(clipTerm) ?? false)
						{
							Program.ManualClip?.Invoke();
							HighlightsHelper.SaveHighlight("PERSONAL_HIGHLIGHT_GROUP", "MANUAL", true);
							Program.synth.SpeakAsync("Clip Saved!");
							return;
						}
					}
				}
			}
			catch (Exception e)
			{
				Logger.LogRow(Logger.LogType.Error, "Error handling voice result: " + e);
			}
		}

		private static int GetMicByName(string name)
		{
			for (int deviceId = 0; deviceId < WaveIn.DeviceCount; deviceId++)
			{
				WaveInCapabilities deviceInfo = WaveIn.GetCapabilities(deviceId);
				if (deviceInfo.ProductName == name)
				{
					return deviceId;
				}
			}

			return 0;
		}

		private static MMDevice GetSpeakerByName(string name)
		{
			MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
			List<MMDevice> devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
			int index = devices.Select(d => d.FriendlyName).ToList().IndexOf(name);
			if (index == -1) index = 0;
			return devices[index];
		}


		public float GetMicLevel()
		{
			return micLevel;
		}

		public float GetSpeakerLevel()
		{
			return speakerLevel;
		}

		public async Task ReloadMic()
		{
			micCapture.StopRecording();
			micCapture.DeviceNumber = GetMicByName(SparkSettings.instance.microphone);
			await Task.Delay(100);
			micCapture.StartRecording();
		}

		public async Task ReloadSpeaker()
		{
			speakerCapture.StopRecording();
			await Task.Delay(100);
			speakerCapture = new WasapiLoopbackCapture(GetSpeakerByName(SparkSettings.instance.speaker));
			speakerCapture.DataAvailable += SpeakerDataAvailable;
			speakerCapture.StartRecording();
		}

	}
}