using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace client_supervisor
{
    public class AnomalyLog
    {
        public int Idx { get; set; } // 로그 인덱스
        public int Idx_Pin { get; set; }    // 핀 번호
        public string Worker { get; set; } // 처리 담당자 이름
        public DateTime Time_Start { get; set; } // 발생 시간
        public DateTime Time_End { get; set; } // 상황 처리 시간
        public string Memo { get; set; } // 설명
        public int Code_Anomaly { get; set; } // 이상 코드

        public AnomalyLog(int idx, int idx_pin, DateTime time_start, string memo)
        {
            Idx = idx;
            Idx_Pin = idx_pin;
            Time_Start = time_start;
            Memo = memo;
        }
    }
}
