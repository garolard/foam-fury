import wave, struct, math, random, os
SR = 22050
random.seed(7)
OUT = os.path.join(os.path.dirname(__file__), '..', 'audio')

def write(name, samples, vol=0.9):
    peak = max(1e-6, max(abs(s) for s in samples))
    k = vol / peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'w') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s * k)) * 32767)) for s in samples))

def env(t, a, d):  # attack/decay exp
    return min(1, t / a) * math.exp(-t / d) if a > 0 else math.exp(-t / d)

class LP:
    def __init__(s, c): s.c = c; s.y = 0
    def __call__(s, x): s.y += s.c * (x - s.y); return s.y

def noise(): return random.uniform(-1, 1)

def shot(dur, f0, f1, nd, td, lp, body=0.6):
    out = []; f = LP(lp); ph = 0
    n = int(dur * SR)
    for i in range(n):
        t = i / SR
        fr = f1 + (f0 - f1) * math.exp(-t / 0.03)
        ph += 2 * math.pi * fr / SR
        s = body * math.sin(ph) * env(t, 0.001, td) + f(noise()) * env(t, 0.0005, nd)
        out.append(s)
    return out

write('shot_pistol', shot(0.25, 900, 160, 0.04, 0.06, 0.5))
write('shot_smg', shot(0.16, 1200, 220, 0.025, 0.04, 0.6), 0.8)
write('shot_shotgun', shot(0.5, 500, 70, 0.12, 0.12, 0.35, 0.9))
write('shot_rifle', shot(0.6, 1600, 90, 0.08, 0.18, 0.45, 0.8))

def thud(dur, f0, d, nz=0.3, lp=0.2):
    out = []; f = LP(lp); ph = 0
    for i in range(int(dur * SR)):
        t = i / SR
        ph += 2 * math.pi * (f0 * (1 + 2 * math.exp(-t / 0.01))) / SR
        out.append(math.sin(ph) * env(t, 0.001, d) + nz * f(noise()) * env(t, 0.001, d * 0.5))
    return out
write('hit', thud(0.12, 180, 0.03, 0.8, 0.5))
write('impact', thud(0.1, 300, 0.02, 0.6, 0.3), 0.5)
write('headshot', [a + 0.5 * math.sin(2*math.pi*1800*i/SR) * math.exp(-i/SR/0.05) for i, a in enumerate(thud(0.2, 220, 0.04, 0.8, 0.5))])

def groan(dur, base, seed):
    random.seed(seed); out = []; ph = 0; f = LP(0.08); f2 = LP(0.02)
    vib = random.uniform(4, 7)
    for i in range(int(dur * SR)):
        t = i / SR
        fr = base * (1 + 0.15 * math.sin(2*math.pi*vib*t) + 0.25 * math.sin(math.pi * t / dur)) + 8 * noise()
        ph += 2 * math.pi * fr / SR
        saw = (ph / math.pi) % 2 - 1
        e = math.sin(math.pi * min(1, t / dur)) ** 0.7
        out.append((f(saw) * 1.5 + 0.3 * f2(noise())) * e)
    return out
for k in range(4):
    write(f'groan{k}', groan(random.uniform(0.9, 1.5), random.uniform(70, 120), k + 10), 0.7)
write('zombie_die', groan(0.7, 160, 99), 0.8)
write('brute_roar', groan(1.4, 55, 5), 0.9)

def sweep(dur, f0, f1, wav='sin', d=None, vib=0):
    out = []; ph = 0
    for i in range(int(dur * SR)):
        t = i / SR; p = t / dur
        fr = f0 * (f1 / f0) ** p
        ph += 2 * math.pi * fr / SR
        s = math.sin(ph) if wav == 'sin' else (1 if math.sin(ph) > 0 else -1) * 0.5
        e = math.exp(-t / d) if d else (1 - p)
        out.append(s * e)
    return out
pk = []
for i, fr in enumerate([660, 880, 1320]):
    pk += [math.sin(2*math.pi*fr*j/SR) * math.exp(-j/SR/0.05) for j in range(int(0.06 * SR))]
write('pickup', pk, 0.6)
write('hurt', [a + 0.5*b for a, b in zip(thud(0.3, 90, 0.1, 1.0, 0.3), sweep(0.3, 400, 150, 'sq'))])
write('empty', thud(0.05, 2000, 0.005, 0.4, 0.9), 0.4)
rl = thud(0.08, 1200, 0.01, 0.7, 0.7) + [0] * int(0.18 * SR) + thud(0.1, 800, 0.015, 0.7, 0.6)
write('reload', rl, 0.5)
def boom():
    out = []; f = LP(0.05); f2 = LP(0.2)
    for i in range(int(1.4 * SR)):
        t = i / SR
        out.append(f(noise()) * 3 * env(t, 0.003, 0.35) + f2(noise()) * env(t, 0.001, 0.05) + math.sin(2*math.pi*(45 + 60*math.exp(-t/0.1))*t) * env(t, 0.002, 0.3))
    return out
write('explosion', boom())
write('dash', [noise() * math.sin(math.pi * i / (0.25 * SR)) for i in range(int(0.25 * SR))] , 0.3)
def dashf():
    f = LP(0.03); x = []
    for i in range(int(0.25*SR)):
        t=i/SR; f.c = 0.02 + 0.3 * t/0.25
        x.append(f(noise()) * math.sin(math.pi*t/0.25))
    return x
write('dash', dashf(), 0.6)
write('throw', dashf()[::-1], 0.4)
write('bounce', thud(0.08, 400, 0.02, 0.2, 0.3), 0.4)
# wave start horn & clear jingle
def tone(seq, d=0.18, wav='sq'):
    out = []
    for fr in seq:
        for j in range(int(d * SR)):
            t = j / SR
            s = sum(math.sin(2*math.pi*fr*h*t)/h for h in (1, 2, 3))
            out.append(s * min(1, t/0.01) * math.exp(-t / (d*0.8)))
    return out
write('wave_start', tone([110, 110, 98, 147], 0.3), 0.7)
write('wave_clear', tone([523, 659, 784, 1047], 0.14), 0.6)
write('ui_move', tone([880], 0.05), 0.3)
write('ui_select', tone([660, 990], 0.07), 0.4)
write('game_over', tone([392, 330, 262, 196], 0.35), 0.7)

# music loop: dark driving beat, 120bpm, 8 bars
def music():
    bpm = 116; beat = 60 / bpm; bars = 8; n = int(bars * 4 * beat * SR)
    out = [0.0] * n
    def add(start, smp, g=1):
        s0 = int(start * SR)
        for j, v in enumerate(smp):
            if s0 + j < n: out[s0 + j] += v * g
    kick = [math.sin(2*math.pi*(50 + 120*math.exp(-j/SR/0.03))*j/SR) * math.exp(-j/SR/0.18) for j in range(int(0.35*SR))]
    f = LP(0.6)
    snare = [f(noise()) * math.exp(-j/SR/0.08) + 0.3*math.sin(2*math.pi*190*j/SR)*math.exp(-j/SR/0.05) for j in range(int(0.25*SR))]
    hat = [noise() * math.exp(-j/SR/0.015) for j in range(int(0.05*SR))]
    hp = 0
    roots = [55, 55, 65.4, 49, 55, 55, 73.4, 61.7]
    for b in range(bars):
        for q in range(4):
            t = (b*4 + q) * beat
            add(t, kick, 0.9 if q in (0, 2) else 0.0)
            if q in (1, 3): add(t, snare, 0.45)
            add(t, hat, 0.15); add(t + beat/2, hat, 0.1)
            if b % 2 == 1 and q == 3: add(t + beat*0.75, kick, 0.6)
        # bass: 8th notes
        r = roots[b]
        lpb = LP(0.12)
        for e8 in range(8):
            fr = r * (2 if e8 in (3, 6) else 1)
            seg = []
            for j in range(int(beat/2*SR)):
                tt = j/SR
                saw = ((fr*tt) % 1) * 2 - 1
                seg.append(lpb(saw) * math.exp(-tt/0.25))
            add(b*4*beat + e8*beat/2, seg, 0.5)
        # eerie pad
        pad_f = r * 4
        for j in range(int(4*beat*SR)):
            tt = j/SR
            v = 0.06 * (math.sin(2*math.pi*pad_f*tt) + math.sin(2*math.pi*pad_f*1.5*tt*1.003)) * math.sin(math.pi * j / (4*beat*SR))
            out[int(b*4*beat*SR) + j] += v
    return out
write('music', music(), 0.6)
print('done')
