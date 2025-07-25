using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Window_Setup_Server.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window_Setup_Server : Window
    {
        public Window_Setup_Server(string IP, int PORT)
        {
            InitializeComponent();

            TextBox_IP.Text = IP;
            TextBox_PORT.Text = PORT.ToString();
        }

        private void pb_ok_Click(object sender, RoutedEventArgs e)
        {
            if (TextBox_IP.Text == "" || TextBox_PORT.Text == "")
            {
                MessageBox.Show("빈칸이 존재합니다.\n다시 시도해주세요.", "서버 주소 수정 경고");
            }
            else
            {
                this.DialogResult = true;
            }
        }

        private void pb_no_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
