# 🧬 Turing Morphogenesis 3D

**Real-time Gray–Scott reaction–diffusion on 3D surfaces — GPU-accelerated, interactive, alive.**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-brightgreen.svg)](LICENSE)
[![DirectX](https://img.shields.io/badge/compute-DirectX%2011-red.svg)]()
[![WPF](https://img.shields.io/badge/UI-WPF-9B4F96.svg)]()

<img width="1575" height="946" alt="{8406C4AB-3503-4646-893C-D4CE2573C168}" src="https://github.com/user-attachments/assets/2c9853f8-71e0-47e2-bf7c-eb557fb6c3c8" />


*Leopard-spot pattern growing on a displaced sphere — computed live, not baked.*

</div>

---

## ✨ What is this?

An interactive laboratory of **Turing morphogenesis**: the Gray–Scott reaction–diffusion
system runs in real time and paints itself onto spheres, tori, cylinders and planes.
Patterns are not textures — they are *alive*: feed them, kill them, paint them,
sculpt them into relief, export them as meshes or video sequences.

```
∂u/t = Du·∇²u − u·v² + f·(1−u)
∂v/∂t = Dv·∇²v + u·v² − (f+k)·v
```

## 🖼 Gallery
<img width="1579" height="902" alt="{C3343D75-B333-4D26-AE74-AA0345F44A7A}" src="https://github.com/user-attachments/assets/f7e7fefe-f072-4d64-99f5-76e24641ea23" />

<img width="1578" height="918" alt="{EC2A228C-102C-4F81-A3C5-FD9463FAC209}" src="https://github.com/user-attachments/assets/4a6bbd47-cdaa-4611-a1e9-8d7301544982" />

<img width="1584" height="909" alt="{C2D3063F-9600-4E0D-8765-0C68477E8497}" src="https://github.com/user-attachments/assets/1a19ff4c-4e99-4705-85d8-f2bf723cc89f" />

## ⚡ Two engines, one simulation

| Engine | Path | Notes |
|---|---|---|
| **GPU** | DirectX 11 compute shader → shared D3D9 surface → `D3DImage` | Zero readback for visuals; colorization on GPU |
| **CPU** | `float32` + `Vector<float>` SIMD, periodic boundaries | Automatic fallback if GPU/interop unavailable |

Plus: per-vertex normal recomputation for relief lighting, adaptive contrast,
ping-pong structured buffers, Pearson viability map precomputed in parallel.

## 🎮 Controls

| Action | Input |
|---|---|
| Rotate / pan / zoom | LMB / RMB / wheel |
| Paint on surface | `Shift` + drag |
| Start / stop · reset | `F5` · `F6` |
| Presentation mode | `⛶` (auto-cycles presets), `Esc` to exit |
| Brushes | Paint · Erase · Smooth · Stamp |

## 🚀 Quick start

```bash
git clone https://github.com/twiks228/TuringMorphogenesis3D.git
cd TuringMorphogenesis3D
dotnet run --configuration Release
```

**Requirements:** Windows 10/11, .NET 8 SDK. GPU is optional but recommended.

## 🧰 Tech stack

`C# / .NET 8` · `WPF` · `HelixToolkit.Wpf` · `SharpDX (D3D11/D3D9/HLSL)` ·
`NAudio` · `System.Numerics SIMD` · `MVVM`

## 🗺 Roadmap

- [ ] Bloom post-processing in the compute pipeline
- [ ] Parameter sweep ("morphing" through phase space) with auto-recording
- [ ] Image seeding: grow patterns from your photos
- [ ] TimeMachine: ring-buffer history with timeline scrubbing
- [ ] Rust + wgpu port (Bevy ECS architecture)

## 📄 License

[MIT](LICENSE) — free for any use, including commercial.

---

<div align="center">

*“The universe is not made of things, but of patterns that persist.”* — after Alan Turing, 1952

</div>

