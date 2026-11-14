# FreCre2

**Dodge. Jump. Survive.**

FreCre2 is a 2D pixel-art survival game developed with Unity and C#.

The objective is simple: survive as long as possible by dodging missiles flying in from all directions. Move freely around a small sandy arena, time your jumps, and challenge yourself to beat your personal best.

As time passes, missiles spawn more frequently, gradually increasing the difficulty.

## Play the Game

**[Play FreCre2 on itch.io](https://neat3.itch.io/frecre2)**

## Features

- **8-Directional Movement** — Move freely using keyboard or virtual joystick controls.
- **Jump & Dodge System** — Avoid incoming missiles by timing your jumps.
- **Dynamic Difficulty** — Missile spawn intervals decrease and the maximum number of active missiles increases over time.
- **Random Missile Spawning** — Missiles spawn from random angles around the arena and travel in straight lines.
- **Survival Record System** — Track your survival time and save your personal best.
- **Pixel-Art Animations** — Directional Idle, Run, Jump, Fall, Death, and Stretching animations.
- **Character Portrait Animations** — Animated portraits that respond to gameplay events.
- **Cross-Platform Controls** — Keyboard controls for PC and virtual joystick controls for mobile.
- **Responsive UI** — A fixed 16:9 gameplay area with letterboxing and safe-area handling.

## Controls

| Action | PC | Mobile |
|---|---|---|
| Move | WASD / Arrow Keys | Virtual Joystick |
| Jump | Spacebar | Jump Button |

## Gameplay

1. Choose PC or Mobile controls from the main menu.
2. Wait for the countdown: **3, 2, 1, START!**
3. Dodge incoming missiles by moving and jumping.
4. Survive as long as possible as the difficulty increases.
5. Beat your personal best and try again!

One hit means **GAME OVER**.

## Technical Overview

**Engine:** Unity 2D  
**Language:** C#  
**Platforms:** Windows / Android / WebGL

### Core Systems

| System | Description |
|---|---|
| GameManager | Manages game states and the overall gameplay flow |
| PlayerController | Handles 8-directional movement, jumping, and ground positioning |
| PlayerInputRouter | Converts keyboard and mobile input into shared movement and jump commands |
| PlayerAnimationController | Controls directional animations and character state transitions |
| PlayerHitDetector | Detects missile collisions and checks jump height for successful dodges |
| MissileSpawner | Spawns missiles at random angles and manages active missiles |
| DifficultyManager | Adjusts missile spawn intervals and active missile limits based on survival time |
| SurvivalTimer | Tracks elapsed survival time |
| RecordStore | Saves and loads the player's best survival record |
| StartCountdownController | Manages the pre-game countdown and start sequence |
| AspectRatioController | Maintains a consistent 16:9 gameplay area across different screen sizes |

### Implementation Highlights

**Ground-Based Collision and Visual Jumping**

The player's ground position and visual jump height are managed separately. The collision area remains on the ground while the character sprite moves upward during a jump.

Missile avoidance is determined by comparing the player's jump height against a dodge-height threshold.

**Dynamic Missile Difficulty**

Difficulty increases over time by reducing missile spawn intervals and increasing the maximum number of active missiles.

Missile speed and direction are determined at spawn time and remain constant during flight.

**Unified Input System**

PC keyboard input and mobile virtual joystick input are converted into the same movement and jump commands.

The mobile joystick supports 8-directional input, a dead zone, and multitouch interaction with the jump button.

**Responsive Display**

The game maintains a fixed 16:9 aspect ratio regardless of the user's screen resolution. Letterboxing, pillarboxing, and safe-area handling preserve the intended gameplay view and UI layout.

## Project Structure

```text
Assets/
├── Scripts/
│   ├── Core/
│   ├── Player/
│   ├── Missile/
│   ├── Input/
│   ├── UI/
│   ├── Effects/
│   └── Services/
└── Tests/
    ├── EditMode/
    └── PlayMode/
```

The project uses a modular architecture with separated responsibilities for gameplay, input, animation, UI, and data persistence.

Unity EditMode and PlayMode tests are used to verify core systems and gameplay behavior.

## Credits

**Development**
- Game Design & Programming: Neat3
- Game Engine: Unity
- Image Assets: PixelLab
- Fonts: BoldPixels by Yūki

## Links & Contact

- **itch.io:** https://neat3.itch.io/frecre2
- **YouTube:** Neat3Develop
- **GitHub:** https://github.com/albertk0305/FreCre2
- **Email:** neat3booze@gmail.com

---

*Thanks for checking out FreCre2!*
