# TamaRush
A Tamagotchi emulator mod for Bomb Rush Cyberfunk

Tamarush is a Bomb Rush Cyberfunk mod that adds in a Tamagotchi emulator, built from the ground up to fit in unity/BRC, while also keeping and even adding onto the core features of the projects it based off, which are: Tamalib, Tamatool, PyTama.

<details>
  <summary><b>Features</b></summary>

  - **Tamagotchi P1 Emulator:** Emulator supports P1 Tamagotchi ROMs
  
  - **Run In Background:** You can have your game be running in the background as you play BRC, allowing it to keep growing. and make noises when it needs you.
  
  - **Speed Settings:** You can set the game to run at either normal, 2x, 4x, 6x speed.
  
  - **Manage Saves:** You can easily delete/back up save files from the phone, allowing you to quickly restart whenever you want

  - **LCD Color Options:** There are multiple color options for the LCDs in the emulator, allowing you to play in what ever style you want.

  - **Swap Between ROMs:** There's an option in game settings that will let you select a rom to run, allowing you to have multiple at one time. (Each ROM gets it's own folder to hold save files in)

  - **Custom Backgrounds/Icons:** You can have multiple options for the background image, and game icons, that you can switch between in the phone.

  - **Audio Control:** You can have the audio for the emulator entirely disabled, or you can set its volume on a 1 - 10 scale.

  - **Pixel Size Control:** You can change the size of pixels, allowing you to zoom in a sense.

  - **All From The In Game Phone:** Everything in this mod, including even opening up the folders, can be done from within the in-game phone.
</details>

<details>
  <summary><b>Images</b></summary>

  - ![](https://github.com/SnailUsbs/TamaRush/blob/main/Images/image3.png)
  - ![](https://github.com/SnailUsbs/TamaRush/blob/main/Images/image4.png)
  - ![](https://github.com/SnailUsbs/TamaRush/blob/main/Images/image.png)
  - ![](https://github.com/SnailUsbs/TamaRush/blob/main/Images/image2.png)
  
</details>

## How To Use:

- **The Tamarush mod can be downloaded from Thunderstore, or directly in your thunderstore based mod managers like R2ModMan**

- **You most get your own legally obtained ROM:** This mod does not have any of the game code, just code to mimic the hardware needed to run the game. You will need to get your own legally obtained rom, and then place it/them in your *BepInEX/TamaRush/Roms* folder.

- **You most get your own legally obtained Background/Icon Images:** While TamaRush has some original icons that are meant to act as place holders, you need to get your own background and icon images, and then put them in *BepInEX/TamaRush/Icons* & *BepInEX/TamaRush/Backgrounds*.

- **Tamarush adds a new phone app to the in-game phone, which is where you can do everything you need to releating to this mod**

- **Controls for the emulator, are the phone buttons:** When you are in the *play* mode, BRC's inputs are rewired to only let you intereact with the emulator, which you with either:
> **- Phone Left:** Left button on a tamagotchi
> **- Phone Center:** Center button on a tamagotchi
> **- Phone Right:** Right button on a tamagotchi
> **- Phone Down:** Tap input in the form of a button (used for Angel devices/roms)

## Important Info:

- **ONLY *".bin"* ROMS Work:** This emulator is desgined to only support *".bin"* based roms.

- **Only 5 Save files, per ROM, will be stored at one time (Not including backups):** For each rom, only 5 save files will be saved at a given time, with new save files always overwriting the oldest one

- **ROM's Save folders, where the save files are stored, are based on the ROM's name:** Your save files for each rom, and the folder they are placed in, will be named after whatever is the rom is named.

## FAQS:

## Credit:
- **Tamalib:** TamaRush's emulator is heavily based off of Tamalib, just built from the ground up to only work in unity/BRC

- **TamaTool:** Extra features in TamaRush like LCD colors, speed, save states, were based on features from TamaTool, which is a stand alone emulator also built on top of Tamalib

- **BRC-CODE-DMG:** After getting stuck on the rendering pipe line between BRC and the emulator, I looked at this project and figured out Delta Transform was my fix. I also used this project to figure out how to wire up audio from the emulator, into BRC.

- **Tamalib:** https://github.com/jcrona/tamalib

- **Tamatool:** https://github.com/jcrona/tamatool/

- **BRC-CODE-DMG:** https://github.com/AbsentmindedGCN/BRC-CODE-DMG/
