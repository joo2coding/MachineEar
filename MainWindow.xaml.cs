using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.Wave;
using ScottPlot;

namespace MachineEar_MIC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private WaveInEvent waveIn;
        private List<float> audioBuffer = new();
        private DispatcherTimer plotTimer;
        private int selectedDeviceIndex = 0;
        private const int SampleRate = 44100;

        public MainWindow()
        {
            InitializeComponent();
            LoadDevices();
            InitMic(selectedDeviceIndex);
        }

        private void LoadDevices()
        {
            comboDevices.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var info = WaveIn.GetCapabilities(i);
                comboDevices.Items.Add($"{i}: {info.ProductName}");
            }
            comboDevices.SelectedIndex = 0;
        }

        private void comboDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedDeviceIndex = comboDevices.SelectedIndex;
            waveIn?.StopRecording();
            waveIn?.Dispose();
            audioBuffer.Clear();
            InitMic(selectedDeviceIndex);
        }

        private void InitMic(int deviceIndex)
        {
            waveIn = new WaveInEvent();
            waveIn.DeviceNumber = deviceIndex;
            waveIn.WaveFormat = new WaveFormat(SampleRate, 1); // 44.1kHz, Mono
            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.StartRecording();

            plotTimer = new DispatcherTimer();
            plotTimer.Interval = TimeSpan.FromMilliseconds(50); // 20fps
            plotTimer.Tick += PlotTimer_Tick;
            plotTimer.Start();
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float normalized = sample / 32768f;
                audioBuffer.Add(normalized);
            }
            if (audioBuffer.Count > SampleRate) // 1초 분량
                audioBuffer = audioBuffer.Skip(audioBuffer.Count - SampleRate).ToList();
        }

        private void PlotTimer_Tick(object sender, EventArgs e)
        {
            if (audioBuffer.Count == 0) return;

            // 파형 표시
            var plotSamples = audioBuffer.Skip(Math.Max(0, audioBuffer.Count - 2000)).ToArray();
            wpfPlot.Plot.Clear();
            wpfPlot.Plot.Add.Signal(plotSamples, color: Colors.DodgerBlue);
            wpfPlot.Plot.Axes.AutoScale();
            wpfPlot.RenderSize = new System.Windows.Size(800, 300);

            // Peak 표시
            float peak = plotSamples.Select(x => Math.Abs(x)).Max();
            txtPeak.Text = $"Peak: {peak:F3}";

            // FFT 표시
            int fftLen = 2048;
            if (plotSamples.Length < fftLen) return;
            var fftInput = plotSamples.Skip(plotSamples.Length - fftLen).Take(fftLen).Select(x => new Complex(x, 0)).ToArray();
            
            //Fourier.Forward(fftInput, FourierOptions.Matlab);
            double[] fftMag = fftInput.Take(fftLen / 2).Select(c => c.Magnitude).ToArray();
            double[] freq = Enumerable.Range(0, fftLen / 2).Select(i => i * SampleRate / (double)fftLen).ToArray();

            wpfPlotFFT.Plot.Clear();
            wpfPlotFFT.Plot.Add.Scatter(freq, fftMag, color: Colors.MediumVioletRed);
            wpfPlotFFT.Plot.Title("FFT (주파수 스펙트럼)");
            wpfPlotFFT.Plot.XLabel("Frequency (Hz)");
            wpfPlotFFT.Plot.YLabel("Magnitude");
            wpfPlotFFT.Plot.Axes.AutoScale();
            wpfPlotFFT.RenderSize = new System.Windows.Size(800, 300);
        }

        protected override void OnClosed(EventArgs e)
        {
            waveIn?.StopRecording();
            waveIn?.Dispose();
            base.OnClosed(e);
        }
    }
}