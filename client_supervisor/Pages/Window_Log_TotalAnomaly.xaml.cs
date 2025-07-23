using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace client_supervisor
{
    /// <summary>
    /// Window_Log_TotalAnomaly.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window_Log_TotalAnomaly : Window
    {
        private Dictionary<int, string> dict_error { get; set; }
        private Dictionary<int, string> dict_proc { get; set; }

        public List<int> List_remove { get; set; } = new List<int>(); // 삭제할 로그 인덱스 목록
        public List<AnomalyLog> List_update { get; set; } = new List<AnomalyLog>(); // 수정할 로그 목록
        private int idx_selected { get; set; } // 선택된 로그의 인덱스

        public event EventHandler<DateTime> Req_Log_Date;
        public ObservableCollection<AnomalyLog> AnomalyLogs { get; set; } = new ObservableCollection<AnomalyLog>();
        private DateTime date_selected;
        private bool flag_init;

        public Window_Log_TotalAnomaly(Dictionary<int, string> dict_error, Dictionary<int, string> dict_proc)
        {
            this.dict_error = dict_error;
            this.dict_proc = dict_proc;

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

            this.DataContext = this;
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
                    
                    // 세부사항 초기화
                   this.ClearAllContext();
                }
                CalendarPopup.IsOpen = false; // 팝업 닫기
            }
        }

        // 선택 항목 삭제 클릭 시 실행
        private void pb_remove_checked_Click(object sender, RoutedEventArgs e)
        {
            List<AnomalyLog> list_remove = new List<AnomalyLog>();
            if (this.AnomalyLogs.Count == 0)
            {
                MessageBox.Show("삭제할 항목이 없습니다.", "삭제 처리");
                return;
            }

            for (int i = 0; i < this.AnomalyLogs.Count; i++)
            {
                if (this.AnomalyLogs[i].IsChecked)
                {
                    list_remove.Add(this.AnomalyLogs[i]);
                    this.List_remove.Add(this.AnomalyLogs[i].Idx); // 삭제할 로그 인덱스 목록에 추가
                }
            }
   
            foreach(AnomalyLog log in list_remove)
            {
                this.AnomalyLogs.Remove(log);
            }
            MessageBox.Show("선택된 항목이 삭제되었습니다.", "삭제 처리");
        }

        // 적용 클릭 시 실행
        private void pb_proc_commit_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("적용하시겠습니까?", "핀 목록 적용", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                this.DialogResult = true;
            }
        }

        public void transit_log(ObservableCollection<AnomalyLog> logs)
        {
            foreach (KeyValuePair<int, string> keyValuePairs in this.dict_error)
            {
                Console.WriteLine($"Key : {keyValuePairs.Key} - Value : {keyValuePairs.Value}");
            }

            this.AnomalyLogs.Clear();
            // 깊은복사 필요
            foreach (var m in logs)
            {
                this.AnomalyLogs.Add(new AnomalyLog(
                    m.Idx,
                    m.Pin.Copy(),
                    m.Time_Start,
                    m.Code_Error,
                    m.Map_Name,
                    m.Worker,
                    m.Time_End,
                    m.Memo,
                    m.Code_Anomaly
                ));

                this.AnomalyLogs.Last().IsChecked = false;
                this.AnomalyLogs.Last().Str_Error = this.dict_error[m.Code_Error];
                //Console.WriteLine($"에러코드 : {m.Str_Error}");
            }
        }

        // DataGrid 선택칸이 바꼈을 때 실행되는 함수
        private void table_total_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
 
            if (table_total.SelectedItem is AnomalyLog selectedLog)
            {
                // 인덱스/객체 다 사용 가능
                int idx = selectedLog.Idx;
                this.idx_selected = table_total.SelectedIndex;
                //Console.WriteLine($"선택: Idx={idx}, 행={index}");
                
                label_data_map.Content = this.AnomalyLogs[this.idx_selected].Map_Name.ToString();
                label_data_pin.Content = this.AnomalyLogs[this.idx_selected].Pin.Idx.ToString();
                label_data_name.Content = this.AnomalyLogs[this.idx_selected].Pin.Name_Pin.ToString();
                label_data_loc.Content = this.AnomalyLogs[this.idx_selected].Pin.Name_Location.ToString();
                label_data_manager.Content = this.AnomalyLogs[this.idx_selected].Pin.Name_Manager.ToString();
                label_start_datetime.Content = this.AnomalyLogs[this.idx_selected].Time_Start.ToString();
                label_proc_datetime.Content = this.AnomalyLogs[this.idx_selected].Time_End.ToString();
                label_proc_error.Content = this.AnomalyLogs[this.idx_selected].Str_Error.ToString();
                // 처리종류는 라디오버튼 wrap_kind_anomaly
                textbox_proc_manager.Text = this.AnomalyLogs[this.idx_selected].Worker.ToString();
                textbox_proc_memo.Text = this.AnomalyLogs[this.idx_selected].Memo.ToString();

                textbox_proc_manager.IsEnabled = true;
                textbox_proc_memo.IsEnabled = true;

                //Console.WriteLine($"Code_Anomaly: {this.AnomalyLogs[this.idx_selected].Code_Anomaly}");

                if (wrap_kind_anomaly.Children.Count == 0)this.CreateRadioButtons_Click(this.dict_proc);
                if(pb_proc_commit.IsEnabled == false) pb_proc_commit.IsEnabled = true; // 적용 버튼 활성화
                if(pb_remove_checked.IsEnabled == false) pb_remove_checked.IsEnabled = true; // 선택 삭제 버튼 활성화
                if(pb_update_checked.IsEnabled == false) pb_update_checked.IsEnabled = true; // 처리 완료 버튼 활성화

                //for(int i = 0; i < wrap_kind_anomaly.Children.Count; i++)
                //{
                //    RadioButton radioButton = wrap_kind_anomaly.Children[i] as RadioButton;
                //    if (radioButton != null)
                //    {
                //        // 라디오 버튼의 인덱스와 로그의 Code_Anomaly를 비교하여 선택 상태 설정
                //        radioButton.IsChecked = (i + 2) == this.AnomalyLogs[this.idx_selected].Code_Anomaly;
                //    }
                //}
                this.RadioGroupChangeState(true); // 라디오 버튼 활성화

                int targetIndex = this.AnomalyLogs[this.idx_selected].Code_Anomaly - 2;
                if (targetIndex >= 0 && targetIndex < wrap_kind_anomaly.Children.Count)
                {
                    if (wrap_kind_anomaly.Children[targetIndex] is RadioButton rb)
                    {
                        rb.IsChecked = true;
                    }
                }
            }
        }


        // 라디오 버튼 동적 생성
        private void AddRadioButtonToPanel(WrapPanel parentPanel, string content, string groupName, bool isChecked = false)
        {
            RadioButton newRadioButton = new RadioButton
            {
                Content = content, // 라디오 버튼에 표시될 텍스트
                GroupName = groupName, // 그룹 이름 설정
                IsChecked = isChecked, // 초기 선택 상태 설정
                Margin = new Thickness(3), // 여백 설정
                FontSize = 12, // 글자 크기 설정
            };

            // 이벤트 핸들러 추가
            newRadioButton.Checked += RadioButton_Checked;
            newRadioButton.IsEnabled = false;
            parentPanel.Children.Add(newRadioButton); // 패널에 라디오 버튼 추가
        }
        // 생성된 라디오버튼 삭제
        private void DeleteRadioButtonToPanel(WrapPanel parentPanel)
        {
            if (parentPanel != null)
            {
                parentPanel.Children.Clear();
            }
        }
        // 라디오 버튼 Checked 이벤트 핸들러
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton? radioButton = sender as RadioButton;
            if (radioButton != null)
            {
                //MessageBox.Show($"{radioButton.Content}이(가) 선택되었습니다.");
            }
        }
        // 라디오 버튼 생성
        private void CreateRadioButtons_Click(Dictionary<int, string> dict_name)
        {
            wrap_kind_anomaly.Children.Clear();

            foreach (KeyValuePair<int, string> name in dict_name)
            {
                // 1번은 상황 발생, 즉 초기값이라 버튼에 추가하지 않음.
                if (name.Key > 1) AddRadioButtonToPanel(wrap_kind_anomaly, name.Value, "Kind_Anomaly");
            }
        }
        // 라디오버튼 활성화, 비활성화
        private void RadioGroupChangeState(bool state = true)
        {
            foreach (UIElement child in wrap_kind_anomaly.Children)
            {
                if (child is RadioButton radioButton)
                {
                    // 라디오버튼 초기화
                    radioButton.IsChecked = false;
                    radioButton.IsEnabled = state;
                }
            }
        }
        // 초기화 함수
        private void ClearAllContext()
        {
            this.DeleteRadioButtonToPanel(wrap_kind_anomaly);

            label_data_map.Content = "";
            label_data_pin.Content = "";
            label_data_name.Content = "";
            label_data_loc.Content = "";
            label_data_manager.Content = "";
            label_start_datetime.Content = "";
            label_proc_datetime.Content = "";
            label_proc_error.Content = "";
            textbox_proc_manager.Text = "";
            textbox_proc_memo.Text = "";

            pb_update_checked.IsEnabled = false; // 처리 완료 버튼 비활성화
            pb_remove_checked.IsEnabled = false; // 선택 삭제 버튼 비활성화
            pb_proc_commit.IsEnabled = false; // 적용 버튼 비활성화
        }

        private void pb_update_checked_Click(object sender, RoutedEventArgs e)
        {
            this.AnomalyLogs[this.idx_selected].Worker = textbox_proc_manager.Text;
            this.AnomalyLogs[this.idx_selected].Memo = textbox_proc_memo.Text;
            foreach (UIElement child in wrap_kind_anomaly.Children)
            {
                if (child is RadioButton radioButton && radioButton.IsChecked == true)
                {
                    // 선택한 라디오 버튼의 인덱스를 가져옴
                    this.AnomalyLogs[this.idx_selected].Code_Anomaly = wrap_kind_anomaly.Children.IndexOf(child)+2;
                    break; // 첫 번째로 선택된 버튼만 처리
                }
            }
            // 선택된 로그를 수정 목록에 추가, 수정 목록에 존재하면 업데이트
            if (this.List_update.Any(log => log.Idx == this.AnomalyLogs[this.idx_selected].Idx))
            {
                // 이미 수정 목록에 존재하는 경우
                var existingLog = this.List_update.First(log => log.Idx == this.AnomalyLogs[this.idx_selected].Idx);
                existingLog.Worker = this.AnomalyLogs[this.idx_selected].Worker;
                existingLog.Memo = this.AnomalyLogs[this.idx_selected].Memo;
                existingLog.Code_Anomaly = this.AnomalyLogs[this.idx_selected].Code_Anomaly;
            }
            else
            {
                // 수정 목록에 추가
                this.List_update.Add(this.AnomalyLogs[this.idx_selected]);
            }

            MessageBox.Show("수정되었습니다.", "수정 완료");
        }


    }
}
