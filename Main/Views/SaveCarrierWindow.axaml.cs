using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SaveVaultApp.ViewModels;
using SaveVaultApp.Models;
using System.Collections.Generic;
using System;

namespace SaveVaultApp.Views
{
    public partial class SaveCarrierWindow : Window
    {
        public SaveCarrierWindow()
        {
            InitializeComponent();
        }
        
        public SaveCarrierWindow(Settings settings, List<ApplicationInfo> applications) : this()
        {
            // Create and set the view model
            DataContext = new SaveCarrierViewModel(settings, applications);
            
            // Position window
            Rect screenSize;
            if (Screens?.Primary?.WorkingArea != null)
            {
                var workingArea = Screens.Primary.WorkingArea;
                screenSize = new Rect(
                    workingArea.X,
                    workingArea.Y,
                    workingArea.Width,
                    workingArea.Height);
            }
            else
            {
                screenSize = new Rect(0, 0, 1000, 800);
            }
            
            // Create centered window
            var windowSize = new Rect(0, 0, 800, 580);
            var position = screenSize.CenterRect(windowSize);
            Position = new PixelPoint((int)position.Left, (int)position.Top);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}