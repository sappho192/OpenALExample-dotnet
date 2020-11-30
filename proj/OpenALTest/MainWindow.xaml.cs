using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenTK.Audio.OpenAL;

namespace OpenALTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ALDevice alDevice;
        private ALContext alContext;
        private Queue<BackgroundWorker> playbackStoppers = new Queue<BackgroundWorker>();
        
        public MainWindow()
        {
            InitializeComponent();
            InitializeOpenAL();
        }

        private unsafe void InitializeOpenAL()
        {
            alDevice = ALC.OpenDevice(null);
            alContext = ALC.CreateContext(alDevice, (int*)null);
            var result = ALC.MakeContextCurrent(alContext);

            var alVersion = AL.Get(ALGetString.Version);
            var alVendor = AL.Get(ALGetString.Vendor);
            var alRenderer = AL.Get(ALGetString.Renderer);

            lbVersion.Content = alVersion;
            lbVendor.Content = alVendor;
            lbRenderer.Content = alRenderer;
        }

        private void btPlay_Click(object sender, RoutedEventArgs e)
        {
            PlayWav(@"Sound\loop.wav");
        }

        private void PlayWav(string filePath)
        {
            IList<short[]> buffers;
            ALFormat alFormat;
            int sampleRate;

            // Get the data from the wave file.
            getData(File.OpenRead(filePath), out buffers, out alFormat, out sampleRate);

            // Get a list of buffer handles from OpenAL.
            var bufferHandles = AL.GenBuffers(buffers.Count);

            // Store all the data in OpenAL buffers.
            for (int i = 0; i < buffers.Count; i++)
                AL.BufferData(bufferHandles[i], alFormat, buffers[i], sampleRate);

            // Get a source from OpenAL.
            var sourceHandle = AL.GenSource();

            // Fill the source with the buffered data.
            AL.SourceQueueBuffers(sourceHandle, bufferHandles.Length, bufferHandles);

            // Play the source.
            
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal,
            new Action(() => {
                lbState.Content = "playing";
            }));
            AL.SourcePlay(sourceHandle);

            BackgroundWorker playbackStopper = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            playbackStopper.DoWork += playbackStopper_DoWork;
            playbackStopper.RunWorkerCompleted += playbackStopper_RunWorkerCompleted;
            playbackStoppers.Enqueue(playbackStopper);
            playbackStopper.RunWorkerAsync(new Tuple<int, int[]>(sourceHandle, bufferHandles));
        }

        private static void getData(Stream stream, out IList<short[]> buffers, out ALFormat alFormat, out int sampleRate)
        {
            using (var reader = new BinaryReader(stream))
            {
                // RIFF header
                var signature = new string(reader.ReadChars(4));
                if (signature != "RIFF")
                    throw new NotSupportedException("Specified stream is not a wave file.");

                reader.ReadInt32(); // riffChunkSize

                var format = new string(reader.ReadChars(4));
                if (format != "WAVE")
                    throw new NotSupportedException("Specified stream is not a wave file.");

                // WAVE header
                var formatSignature = new string(reader.ReadChars(4));
                if (formatSignature != "fmt ")
                    throw new NotSupportedException("Specified wave file is not supported.");

                int formatChunkSize = reader.ReadInt32();
                reader.ReadInt16(); // audioFormat
                int numChannels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byteRate
                reader.ReadInt16(); // blockAlign
                int bitsPerSample = reader.ReadInt16();

                if (formatChunkSize > 16)
                    reader.ReadBytes(formatChunkSize - 16);

                var dataSignature = new string(reader.ReadChars(4));

                if (dataSignature != "data")
                    throw new NotSupportedException("Only uncompressed wave files are supported.");

                var dataLength = reader.ReadInt32(); // dataChunkSize

                alFormat = getSoundFormat(numChannels, bitsPerSample);

                var data = reader.ReadBytes(dataLength);
                buffers = new List<short[]>();
                int count;
                int i = 0;
                const int bufferSize = 16384;

                while ((count = (Math.Min(data.Length, (i + 1) * bufferSize * 2) - i * bufferSize * 2) / 2) > 0)
                {
                    var buffer = new short[bufferSize];
                    convertBuffer(data, buffer, count, i * bufferSize * 2);
                    buffers.Add(buffer);
                    i++;
                }
            }
        }

        public static ALFormat getSoundFormat(int channels, int bits)
        {
            switch (channels)
            {
                case 1: return bits == 8 ? ALFormat.Mono8 : ALFormat.Mono16;
                case 2: return bits == 8 ? ALFormat.Stereo8 : ALFormat.Stereo16;
                default: throw new NotSupportedException("The specified sound format is not supported.");
            }
        }

        private static void convertBuffer(byte[] inBuffer, short[] outBuffer, int length, int inOffset = 0)
        {
            for (int i = 0; i < length; i++)
                outBuffer[i] = BitConverter.ToInt16(inBuffer, inOffset + 2 * i);
        }

        private void playbackStopper_DoWork(object sender, DoWorkEventArgs e)
        {
            var playbackStopper = (BackgroundWorker)sender;
            var handles = (Tuple<int, int[]>)e.Argument;
            // Wait until we're done playing.
            while (AL.GetSourceState(handles.Item1) != ALSourceState.Stopped) { 
                if(playbackStopper.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
                System.Threading.Thread.Sleep(100);
            }
            e.Result = handles;
            AL.DeleteSource(handles.Item1);
            AL.DeleteBuffers(handles.Item2);
        }

        private void playbackStopper_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal,
            new Action(() =>
            {
                lbState.Content = "stopped";
            }));
        }

        private void btStop_Click(object sender, RoutedEventArgs e)
        {
            while (playbackStoppers.Count != 0)
            {
                var playbackStopper = playbackStoppers.Dequeue();
                if (playbackStopper.IsBusy)
                {
                    playbackStopper.CancelAsync();
                }
            }
        }
    }
}
