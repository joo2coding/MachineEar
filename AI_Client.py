import socket
import json
import struct
import librosa
import torch
import numpy as np
from model import Wavegram_AttentionMap
import time   # 시간 측정용

# 모델 불러오기
ckpt_path = 'MACHINE_EAR_MODEL.ckpt'
model = Wavegram_AttentionMap.load_from_checkpoint(ckpt_path, h=128, lr=0.001)
model.eval()
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
model = model.to(device)

CLASS_LABELS = [
    "fan normal", "fan abnormal",
    "pump normal", "pump abnormal",
    "slider normal", "slider abnormal",
    "valve normal", "valve abnormal",
    "gearbox normal", "gearbox abnormal",
    "ToyConveyor normal", "ToyConveyor abnormal"
]

CLASS = ['fan', 'pump', 'slider', 'valve', 'gearbox', 'ToyConveyor']

# 서버에게 data 보내기 함수
def send_data(sock, json_):  # 4바이트(전체크기) + 4바이트(json크기) + 실제 데이터(json)
    try:
        json_str = json.dumps(json_)
        print(f'json_str : {json_str}')
        json_bytes = json_str.encode('utf-8')  # json의 바이트 배열
        total_bytes = len(json_bytes)          # 보낼 데이터의 총 크기 (json 만 보낼 경우)
        print(f'total_bytes : {total_bytes}')
        # 4바이트로 길이 인코딩 (네트워크 바이트 오더, big-endian)
        header_total = struct.pack('>I', total_bytes)
        header_json = struct.pack('>I', len(json_bytes))   # ← 여기!
        sock.sendall(header_total + header_json + json_bytes)
        print(header_total + header_json + json_bytes)
    except Exception as e:
        print('전송 중 에러:', e)

# 서버로부터 모든 데이터가 전송되었는지 확인하는 함수
def chk_receive_data(sock, bytes_):

    buf = b'' # 바이너리로 읽음
    while len(buf) < bytes_: # 서버에게 받은 총 데이터 길이만큼 읽을 때까지 반복
        print('아직 데이터 다 안왔음')
        chunk = sock.recv(bytes_ - len(buf))
        print('1')
        if not chunk: # 연결이 끊어진 경우
            raise ConnectionError('서버와 연결이 끊어졌습니다!')
        print('2')
        buf += chunk # 데이터를 다 더함
        print('3')
    return buf

# 서버에게 받은 데이터 파싱 함수
def receive_data(sock):
    print('서버한테 데이터가 왔어요!')
    try:
        # header 8바이트 수신하기
        header = chk_receive_data(sock, 8)  # 앞에서 8바이트 읽어서 저장 ---> header(4+4)
        print(f'header : {header}')
        total_len, json_len = struct.unpack('>II', header)  # 4바이트씩 뜯어서 ---> 전체 길이 / json 길이 저장
        print(f'총 데이터 길이(total_len) : {total_len}')
        print(f'json 길이(json_len)      : {json_len}')

        # header 뒤 실제 데이터 수신하기
        body = chk_receive_data(sock, total_len)
        print(f'body : {body}')
        # json과 data 각각 저장하기
        json_ = body[:json_len].decode("utf-8")
        if total_len > json_len: # 데이터가 있는 경우
            data_ = body[json_len:total_len]
        else: # 데이터가 없는 경우
            data_ = None

        print(f'json_data : {json_}')
        if data_ is not None:
            print(f'data : {data_}')

        return json_, data_

    except Exception as e:
        print('수신 에러: ', e)
        return None, None

# 데이터 예측하는 함수
def pred_audio(SR, TIME, model):

    wav_path = "recv.wav"

    # 오디오 파일 읽기
    y, _ = librosa.load(wav_path, sr=SR, duration=TIME)
    # (길이 맞추기: TIME*SR보다 짧으면 0-padding, 길면 잘라내기)
    target_len = int(SR * TIME)
    if len(y) < target_len:
        y = np.pad(y, (0, target_len - len(y)))
    else:
        y = y[:target_len]

    # [batch, sample] 형태로 변환
    x = torch.tensor(y, dtype=torch.float32).unsqueeze(0)

    # 더미 라벨 생성 (12-class 분류이므로, 0~11 중 아무거나. 보통 0)
    dummy_label = torch.tensor([0], dtype=torch.long)

    x = x.to(device)
    dummy_label = dummy_label.to(device)

    # --- 예측 시간 측정 시작 ---
    start = time.time()

    with torch.no_grad():
        logits_tuple = model(x, dummy_label)
        logits = logits_tuple[0] if isinstance(logits_tuple, tuple) else logits_tuple
        pred = torch.argmax(logits, dim=1).item()
        pred_str = CLASS_LABELS[pred]

    # --- 예측 시간 측정 끝 ---
    elapsed = time.time() - start

    print(f"예측 결과: {pred_str}")
    print(f"예측에 걸린 시간: {elapsed:.4f}초")
    return pred_str

# 접속 요청 함수
def proc_2_0_0(sock, conn_switch):
    while conn_switch == 0:
        send_data(sock, {'PROTOCOL': '2-0-0'})  # 접속 요청 데이터 보내기
        json_, data_ = receive_data(sock)
        json_obj = json.loads(json_)
        print(f'json_ : {json_obj}')
        if json_obj['PROTOCOL'] == '2-0-0':
            if json_obj['RESPONSE'] == 'NO':
                print('서버한테 connect NO 받았어요')
                continue
            else:
                print('서버한테 connect OK 받았어요')
                conn_switch = 1
    return conn_switch

# 오디오 수신 함수
def proc_2_1_0(sock, json_obj, data_):
    fail_switch = 0
    if (not 'MAC' in json_obj) or (not '__META__' in json_obj):
        fail_switch = 1
    if '__META__' in json_obj:
        META = json_obj['__META__']
        if (not 'SIZE' in META) or (not 'SAMPLING_RATE' in META) or (not 'TIME' in META):
            fail_switch = 1
    if len(data_) == 0:
        fail_switch = 1

    if fail_switch == 0:  # 데이터를 정상적으로 수신한 경우
        res_json = {'RESPONSE': 'OK'}
        send_data(sock, res_json)
        MAC = json_obj['MAC']
        SIZE = json_obj['__META__']['SIZE']
        SR = json_obj['__META__']['SAMPLING_RATE']
        TIME = json_obj['__META__']['TIME']

        # 서버에서 받은 data_: bytes (WAV 파일) ---> 저장해야함.
        with open("recv.wav", "wb") as f:
            f.write(data_)

        # 오디오 데이터 예측해서 서버에게 예측 결과 송신
        pred_ = pred_audio(SR, TIME)
        class_, result_ = pred_.split()

        result_json = {"PROTOCOL": "2-2-0", "NUM_PIN": MAC, "CLASS": CLASS.index(class_), "RESULT": result_.upper()}
        send_data(sock, result_json)  # 예측 결과 보내기

    # else:
    #     res_json = {'RESPONSE': 'NO'}
    #     send_data(sock, res_json)

# 서버가 예측 결과 받았는지 확인하는 함수
def proc_2_2_0(json_obj):
    if json_obj['RESPONSE'] == 'OK':
        print('예측 결과 잘 받았대요')
    else:
        print('예측 결과 전송이 잘 안됐나봐요')

def main():
    IP = '10.10.20.111'
    PORT = 9000

    client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client_socket.connect((IP, PORT))
    client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    print(f'서버 {IP}:{PORT}에 연결됨')

    conn_switch = 0
    try:
        while True:
            # 접속 요청하기
            if conn_switch == 0:
                conn_switch = proc_2_0_0(client_socket, conn_switch)
            # send_data(client_socket, {'PROTOCOL': '2-1-0'}) # 이것은 테스트용임 ---> 나중에 삭제
            json_, data_ = receive_data(client_socket)
            json_obj = json.loads(json_)
            protocol = json_obj['PROTOCOL']
            # 오디오 데이터 수신하기
            if protocol == '2-1-0':
                proc_2_1_0(client_socket, json_obj, data_)
            # 오디오 데이터 예측 결과 송신하고 받은 메시지
            # elif protocol == '2-2-0':
            #     proc_2_2_0(json_obj)

    except KeyboardInterrupt:
        print('\n사용자 중단')
    finally:
        client_socket.close()
        print('연결 종료')

if __name__ == "__main__":
    main()