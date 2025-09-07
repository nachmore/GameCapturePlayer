using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace GameCapturePlayer
{
    public partial class IntroOverlay : System.Windows.Controls.UserControl
    {
        public IntroOverlay()
        {
            InitializeComponent();
            // Start animations when loaded
            Loaded += (s, e) =>
            {
                try { StartAnimations(); } catch { }
            };
        }

        public void StartAnimations()
        {
            try
            {
                if (Root.Resources["IntroRotateStoryboard"] is Storyboard rotate)
                {
                    // Begin against the UserControl so TargetName resolves to elements in this XAML namescope
                    rotate.Begin(this, true);
                }
            }
            catch { }
        }

        public void StopAnimations()
        {
            try
            {
                if (Root.Resources["IntroRotateStoryboard"] is Storyboard rotate)
                {
                    rotate.Stop(this);
                }
            }
            catch { }
        }

        public async Task FadeOutAsync()
        {
            try
            {
                if (Root.Resources["IntroFadeOutStoryboard"] is Storyboard fade)
                {
                    Storyboard.SetTarget(fade, Root);
                    var tcs = new TaskCompletionSource<bool>();
                    void OnCompleted(object? s, EventArgs e)
                    {
                        try { fade.Completed -= OnCompleted; } catch { }
                        tcs.TrySetResult(true);
                    }
                    fade.Completed += OnCompleted;
                    fade.Begin(Root, true);
                    await tcs.Task.ConfigureAwait(true);
                }
            }
            catch { }
            finally
            {
                try { StopAnimations(); } catch { }
            }
        }
    }
}
