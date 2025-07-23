using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace client_supervisor
{
    public class AnomalyLog
    {
<<<<<<< HEAD
        public ClientPin Pin;
=======
        //public ClientPin Pin;
        //public int Idx { get; set; } // 로그 인덱스
        //public string Worker { get; set; } // 처리 담당자 이름
        //public DateTime Time_Start { get; set; } // 발생 시간
        //public DateTime Time_End { get; set; } // 상황 처리 시간
        //public string Memo { get; set; } // 설명
        //public int Code_Anomaly { get; set; } // 이상 코드

        //public AnomalyLog(int idx, ClientPin pin, DateTime time_start, string worker = "", DateTime time_end = default, string memo = "", int code_anomaly = 1)
        //{
        //    Pin = pin;
        //    Idx = idx;
        //    Worker = worker;
        //    Time_Start = time_start;
        //    Time_End = time_end;
        //    Memo = memo;
        //    Code_Anomaly = code_anomaly;
        //}



        public ClientPin Pin { get; set; }
        //public ClientPin Pin; // ------> 원래 이거였음
>>>>>>> origin/Boeun_Supervisor
        public int Idx { get; set; } // 로그 인덱스
        public string Worker { get; set; } // 처리 담당자 이름
        public DateTime Time_Start { get; set; } // 발생 시간
        public DateTime Time_End { get; set; } // 상황 처리 시간
        public string Memo { get; set; } // 설명
        public int Code_Anomaly { get; set; } // 이상 코드
        public string Map_Name { get; set; } // 맵 이름
        public int Code_Error { get; set; } // 원인 코드
        public string Str_Error { get; set; }
<<<<<<< HEAD
        public bool IsChecked {  get; set; }
=======
        public bool IsChecked { get; set; }
>>>>>>> origin/Boeun_Supervisor

        public AnomalyLog(int idx, ClientPin pin, DateTime time_start, int code_error = 1, string map_name = "", string worker = "", DateTime time_end = default, string memo = "", int code_anomaly = 1)
        {
            Pin = pin;
            Idx = idx;
            Worker = worker;
            Time_Start = time_start;
            Time_End = time_end;
            Memo = memo;
            Code_Anomaly = code_anomaly;
            Map_Name = map_name;
            Code_Error = code_error;
        }
    }
}
