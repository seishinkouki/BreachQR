﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BreachQR
{
    /// <summary>
    /// Receiver.xaml 的交互逻辑
    /// </summary>
    public partial class Receiver : Page
    {
        public Receiver()
        {
            InitializeComponent();
        }
        private void button_close_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }
    }
}
