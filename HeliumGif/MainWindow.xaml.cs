using ByteSizeLib;
using FFMpegCore;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HeliumGif
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DispatcherTimer timerPosition = new DispatcherTimer();
        OpenFileDialog openFileDialog = new OpenFileDialog();
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        TimeSpan videoStart = new TimeSpan(0);
        TimeSpan videoEnd = new TimeSpan(0);
        TimeSpan videoLength = new TimeSpan(0);
        bool converting = false;
        bool sliderPositionDragging = false;
        char[] loadAnim = { '◜', '◝', '◞', '◟' };
        int loadAnimIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            timerPosition.Interval = new TimeSpan(0, 0, 0, 0, 100);
            timerPosition.Tick += timerPosition_Tick;
            timerPosition.Start();
            textBoxStart.Text = TimeSpanToString(videoStart);
            textBoxEnd.Text = TimeSpanToString(videoEnd);
            String tempFolderPath = Path.Combine(Path.GetTempPath(), "ffmpeg");
            String ffmpegPath = Path.Combine(tempFolderPath, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                Directory.CreateDirectory(tempFolderPath);
                byte[] bytes = (byte[])Properties.Resources.ResourceManager.GetObject("ffmpeg");
                using (FileStream fileStream = new FileStream(ffmpegPath, FileMode.Create, FileAccess.Write))
                {
                    fileStream.Write(bytes, 0, bytes.Length);
                }
            }
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = tempFolderPath, TemporaryFilesFolder = tempFolderPath });
        }

        private void timerPosition_Tick(object? sender, EventArgs e)
        {
            textBoxPosition.Text = TimeSpanToString(mediaElement.Position);
            sliderPosition.Value = TimeSpanToPercent(mediaElement.Position, videoLength);
            if (mediaElement.Position >= videoEnd)
            {
                mediaElement.Position = videoStart;
            }
            if (converting)
            {
                labelStatus.Content = labelStatus.Content.ToString().Remove(11, 1);
                labelStatus.Content = labelStatus.Content.ToString().Insert(11, loadAnim[loadAnimIndex].ToString());
                if (loadAnimIndex < 3)
                {
                    ++loadAnimIndex;
                }
                else
                {
                    loadAnimIndex = 0;
                }
            }
        }

        private void buttonChoose_Click(object sender, RoutedEventArgs e)
        {
            openFileDialog.Filter = "(*.mp4;*.mov;*.mkv)|*.mp4;*.mov;*.mkv";
            switch (openFileDialog.ShowDialog())
            {
                case true:
                    mediaElement.Source = new Uri(openFileDialog.FileName);
                    mediaElement.Play();
                    break;
            }
        }

        private void buttonSave_Click(object sender, RoutedEventArgs e)
        {
            if (openFileDialog.FileName.Length > 0)
            {
                saveFileDialog.Filter = "(*.gif)|*.gif";
                int maxMB = int.Parse(comboBoxSize.SelectedValue.ToString().Substring(0, 2));
                switch (saveFileDialog.ShowDialog())
                {
                    case true:
                        Convert(maxMB);
                        break;
                }
            }
        }

        private void textBoxStart_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TimeSpan.TryParse(textBoxStart.Text, out videoStart))
            {
                mediaElement.Position = videoStart;
            }
        }

        private void textBoxEnd_TextChanged(object sender, TextChangedEventArgs e)
        {
            TimeSpan.TryParse(textBoxEnd.Text, out videoEnd);
        }

        private void sliderPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sliderPositionDragging)
            {
                mediaElement.Position = PercentToTimeSpan(sliderPosition.Value, videoEnd);
            }
        }

        private void sliderPosition_DragStarted(object sender, DragStartedEventArgs e)
        {
            sliderPositionDragging = true;
            mediaElement.Stop();
        }

        private void sliderPosition_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            sliderPositionDragging = false;
            mediaElement.Play();
        }

        private void mediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            videoLength = mediaElement.NaturalDuration.TimeSpan;
            videoEnd = videoLength;
            textBoxEnd.Text = TimeSpanToString(videoEnd);
        }

        private void mediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            mediaElement.Position = videoStart;
        }

        private async void Convert(double maxMB)
        {
            labelStatus.Foreground = Brushes.DodgerBlue;
            labelStatus.Content = "Converting -";
            converting = true;
            double fileSize = maxMB;
            int framerate = 30;
            int scale = 800;
            while (fileSize >= maxMB)
            {
                await ConvertToGif(framerate, scale);
                fileSize = ByteSize.FromBytes(new FileInfo(saveFileDialog.FileName).Length).MebiBytes;
                if (fileSize >= maxMB)
                {
                    if (framerate > 15) framerate -= 2;
                    if (scale > 200) scale = (int)(scale * 0.8);
                    else
                    {
                        converting = false;
                        labelStatus.Foreground = Brushes.Red;
                        labelStatus.Content = "Error, try to make video shorter";
                        break;
                    }
                    labelStatus.Content = "Converting - | (fps: " + framerate + ", scale: " + scale + ")";
                }
            }
            if (fileSize < maxMB)
            {
                converting = false;
                labelStatus.Foreground = Brushes.Green;
                labelStatus.Content = "Done - " + Math.Round(fileSize, 2) + " MB | (fps: " + framerate + ", scale: " + scale + ")";
            }
        }

        private async Task<bool> ConvertToGif(int framerate, int scale)
        {
            await FFMpegArguments.FromFileInput(openFileDialog.FileName).OutputToFile(saveFileDialog.FileName, true, options => options
                .WithCustomArgument("-ss " + TimeSpanToString(videoStart) + " -to " + TimeSpanToString(videoEnd)
                    + " -vf \"fps=" + framerate + ",scale=" + scale + ":-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\""))
                .ProcessAsynchronously();
            return true;
        }

        private String TimeSpanToString(TimeSpan timeSpan, bool longFormat = false)
        {
            String format = "HH:mm:ss.f";
            if (longFormat) format = "HH:mm:ss.fff";
            return new DateTime(timeSpan.Ticks).ToString(format);
        }

        private double TimeSpanToPercent(TimeSpan timeSpan, TimeSpan timeSpanEnd)
        {
            if (timeSpanEnd.Ticks > 0)
            {
                return (double)timeSpan.Ticks / timeSpanEnd.Ticks * 100;
            }
            else
            {
                return 0;
            }
        }

        private TimeSpan PercentToTimeSpan(double percent, TimeSpan timeSpanEnd)
        {
            return new TimeSpan((long)(percent / 100 * timeSpanEnd.Ticks));
        }
    }
}