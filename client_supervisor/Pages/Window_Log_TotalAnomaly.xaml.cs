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
using System.Windows.Media.Effects;

namespace client_supervisor
{
    /// <summary>
    /// Window_Log_TotalAnomaly.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window_Log_TotalAnomaly : Window
    {
        public event EventHandler<DateTime> Req_Log_Date;

        private DateTime date_selected;
        private bool flag_init;
        public Window_Log_TotalAnomaly()
        {
            this.flag_init = false;
            if (!this.Resources.Contains("MaterialDesignShadowDepth2"))
            {
                var shadowEffect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    Direction = 270,
                    ShadowDepth = 3,
                    Opacity = 0.2,
                    Color = Colors.Black // System.Windows.Media.Colors 사용
                };
                this.Resources.Add("MaterialDesignShadowDepth2", shadowEffect);
            }
            
            InitializeComponent();
            this.date_selected = DateTime.Today;
            label_dt_set.Content = this.date_selected.ToShortDateString(); // 선택된 날짜를 TextBlock에 표시
        }

        private void pb_open_calander_Click(object sender, RoutedEventArgs e)
        {
            MyCalendar.SelectedDate = this.date_selected;
            label_dt_set.Content = MyCalendar.SelectedDate.Value.ToShortDateString(); // 선택된 날짜를 TextBlock에 표시
            CalendarPopup.IsOpen = true; // 팝업 열기
        }

        private void MyCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            // 캘린더에서 날짜가 선택되었을 때
            if (MyCalendar.SelectedDate.HasValue)
            {
                DateTime selectedDate = MyCalendar.SelectedDate.Value;
                if (selectedDate >= DateTime.Today && this.flag_init)
                {
                    MessageBox.Show("오늘 이전의 기록만 조회가 가능합니다.\n다시 시도해주세요.", "날짜 오류");
                }
                else
                {
                    label_dt_set.Content = selectedDate.ToShortDateString(); // 선택된 날짜를 TextBlock에 표시
                    
                    if (selectedDate != this.date_selected) Req_Log_Date?.Invoke(this, selectedDate);     // 이벤트 발생시켜 선택한 날짜 전달
                    this.date_selected = selectedDate;
                    this.flag_init = true;
                }
                CalendarPopup.IsOpen = false; // 팝업 닫기
            }
        }

        // 선택 항목 삭제 클릭 시 실행
        private void pb_remove_checked_Click(object sender, RoutedEventArgs e)
        {

        }

        // 적용 클릭 시 실행
        private void pb_proc_commit_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
