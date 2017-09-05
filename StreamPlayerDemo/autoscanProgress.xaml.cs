using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CameraApplication
{
    /// <summary>
    /// Interaction logic for autoscanProgress.xaml
    /// </summary>
    public partial class autoscanProgress : Window
    {
        static int prog = 0;

        public autoscanProgress()
        {
            InitializeComponent();
        }

        public void SetProgress(int progress)
        {
            autoscanProgressBar.Value = progress;
             if (progress > 95)
            {
                this.Close();
            }

        }

        private void autoscanProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Dispatcher.Invoke(
            new System.Action(() => autoscanProgressBar.Value = prog)
            );
            if (autoscanProgressBar.Value > 99)
                this.Close();
        }
    }
}
