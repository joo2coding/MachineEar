using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// Window_Add_Pin.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window_Add_Pin : Window
    {
        private ObservableCollection<string> macList;
        public string MAC_Selected
        {
            get { return ComboBox_MAC.SelectedItem as string; }
            set { ComboBox_MAC.SelectedItem = value; }
        }

        public Window_Add_Pin(ObservableCollection<string> macList)
        {
            InitializeComponent();
            ComboBox_MAC.Items.Clear();
            this.macList = macList;
            
            // 콤보박스에 MAC 주소 목록 추가
            foreach (var mac in macList)
            {
                ComboBox_MAC.Items.Add(mac);
            }
        }

        private void pb_ok_Click(object sender, RoutedEventArgs e)
        {
            if(TextBox_Name.Text == "" || TextBox_Manager.Text == "" || TextBox_Location.Text == "" || ComboBox_MAC.Text == "")
            {
                MessageBox.Show("빈칸이 존재합니다.\n다시 시도해주세요.", "핀 추가 경고");
            }
            else
            {   // 빈칸이 없어야만 핀 추가 가능
                this.DialogResult = true;
            }
        }

        private void pb_cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
