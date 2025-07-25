using Microsoft.Win32;
using System;
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
using System.Windows.Shapes;

namespace client_supervisor
{
    /// <summary>
    /// Window_Add_Map.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window_Add_Map : Window
    {
        public Window_Add_Map()
        {
            InitializeComponent();
        }

        private void pb_ok_Click(object sender, RoutedEventArgs e)
        {
            if(textbox_path.Text == "" || textbox_name.Text == "")
            {
                MessageBox.Show("빈칸이 존재합니다.\n다시 시도해주세요.", "지도 추가 경고");
            }
            else
            {
                this.DialogResult = true;
            }
        }

        private void pb_cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void pb_browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dig = new OpenFileDialog();
            dig.Filter = "Image files (*.png 혹은 *.jpg, *.jpeg)|*.png;*.jpg;*jpeg";

            bool? result = dig.ShowDialog();

            if (result == true)
            {
                textbox_path.Text = dig.FileName;
            }
        }
    }
}
