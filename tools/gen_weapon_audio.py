"""Procedural sounds for the signature weapons (Mug Gun, Scorcher).

Standalone so regenerating these doesn't touch the other audio files.
Run: python3 tools/gen_weapon_audio.py
"""
import wave, struct, math, random, os
SR = 22050
random.seed(11)
OUT = os.path.join(os.path.dirname(__file__), '..', 'audio')

def write(name, samples, vol=0.9):
    peak = max(1e-6, max(abs(s) for s in samples))
    k = vol / peak
    with wave.open(os.path.join(OUT, name + '.wav'), 'w') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s * k)) * 32767)) for s in samples))

class LP:
    def __init__(s, c): s.c = c; s.y = 0
    def __call__(s, x): s.y += s.c * (x - s.y); return s.y

def noise(): return random.uniform(-1, 1)

# Mug Gun shot: a wet "thoomp" -- low pitch drop with gurgly filtered noise
def shot_mud():
    out = []; f = LP(0.12); ph = 0
    for i in range(int(0.35 * SR)):
        t = i / SR
        fr = 70 + 160 * math.exp(-t / 0.05) + 25 * math.sin(2 * math.pi * 38 * t)
        ph += 2 * math.pi * fr / SR
        e = min(1, t / 0.004) * math.exp(-t / 0.09)
        out.append(math.sin(ph) * e + 0.6 * f(noise()) * min(1, t / 0.002) * math.exp(-t / 0.05))
    return out

# Glob landing: short squelch made of a few bubbly pops over muffled noise
def splat():
    out = []; f = LP(0.25); ph = 0
    for i in range(int(0.3 * SR)):
        t = i / SR
        fr = 300 * math.exp(-t / 0.04) + 90 + 60 * math.sin(2 * math.pi * 55 * t)
        ph += 2 * math.pi * fr / SR
        out.append(0.5 * math.sin(ph) * math.exp(-t / 0.05) + f(noise()) * min(1, t / 0.003) * math.exp(-t / 0.07))
    return out

# Flamethrower: roaring band-limited noise burst (played every ~0.18 s while firing)
def flame():
    out = []; lo = LP(0.18); lo2 = LP(0.04); n = int(0.26 * SR)
    for i in range(n):
        t = i / SR
        e = math.sin(math.pi * i / n) ** 0.6
        flutter = 1 + 0.35 * math.sin(2 * math.pi * 31 * t + 2 * math.sin(2 * math.pi * 7 * t))
        out.append((lo(noise()) * 0.8 + lo2(noise()) * 1.6) * e * flutter)
    return out

# Puddle catching fire: rising whoomp
def ignite():
    out = []; f = LP(0.05); g = LP(0.3); n = int(0.7 * SR)
    for i in range(n):
        t = i / SR
        e = min(1, t / 0.06) * math.exp(-t / 0.25)
        f.c = 0.03 + 0.25 * min(1, t / 0.15)
        out.append(f(noise()) * 2.0 * e + 0.3 * g(noise()) * e)
    return out

write('shot_mud', shot_mud(), 0.85)
write('splat', splat(), 0.7)
write('flame', flame(), 0.6)
write('ignite', ignite(), 0.85)
