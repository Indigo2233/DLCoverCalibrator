#!/usr/bin/env python3
import fcntl, struct, time, os, select

fd = os.open('/dev/ttyUSB0', os.O_RDWR | os.O_NOCTTY | os.O_NONBLOCK)

# Set baud rate
import termios
attrs = termios.tcgetattr(fd)
attrs[2] = attrs[2] & ~termios.CSTOPB  # 1 stop bit
attrs[2] = attrs[2] & ~termios.PARENB # no parity
attrs[4] = termios.B115200
attrs[5] = termios.B115200
termios.tcsetattr(fd, termios.TCSANOW, attrs)

# Toggle DTR to reset
TIOCM_DTR = 0x002
TIOCM_RTS = 0x004
buf = bytearray(4)
buf[0] = 0  # DTR low
buf[1] = 0
fcntl.ioctl(fd, 0x541A, bytes(buf))  # TIOCMSET
time.sleep(0.1)
buf[0] = TIOCM_DTR | TIOCM_RTS  # DTR high, RTS high
fcntl.ioctl(fd, 0x541A, bytes(buf))
time.sleep(4)

# Read boot
data = b''
while True:
    r, _, _ = select.select([fd], [], [], 0.3)
    if not r: break
    try:
        chunk = os.read(fd, 1024)
        if chunk: data += chunk
    except: break
print('BOOT:', repr(data))

# Helper to send cmd and read response
def send_cmd(cmd_bytes):
    os.write(fd, cmd_bytes)
    time.sleep(0.4)
    data = b''
    for _ in range(8):
        r, _, _ = select.select([fd], [], [], 0.15)
        if r:
            try:
                chunk = os.read(fd, 256)
                if chunk: data += chunk
            except: pass
    return data

# Handshake
print('Z:', repr(send_cmd(b'<Z>')))

# Poll state
print('P:', repr(send_cmd(b'<P>')))

# Poll calibrator
print('L:', repr(send_cmd(b'<L>')))

# SyncAngles simulation
for cmd in [b'<UO0>', b'<UC180>', b'<VO0>', b'<VC180>']:
    print(f'{cmd}:', repr(send_cmd(cmd)))

# Rapid polling like ASCOM
for i in range(10):
    p = send_cmd(b'<P>')
    print(f'P{i}:', repr(p))
    time.sleep(0.5)

# Final state
print('FINAL P:', repr(send_cmd(b'<P>')))

os.close(fd)
print('DONE - ESP8266 survived all tests')
