# RTS AI
DEPREZ Nicolas
GAVELLE Antony
GRONDIN Jérémy

## Presentation
The goal of the project was to implement the AI part in a RTS template. The AI is multi layered (strategic, squad, unit) 

## Features

### Unit 
- unit and squad can be set in different mode, agressive, deffensive, flee, and followInstruction

### Squad
- Units can group up in squad to make differents actions (Attack, capture)
- Squads have 2 formations, circle and line. The player have to press a key to change the formation (k for circle and l for line)

### Turret 
- Player and AI can build turrets to help them defend their bases. They attack an enemy unit when it's too close 

### Miner
- Some building can, after being captured, be upgrade to produce resources

### AI Controller
- The enemy team is controlled by an AI that will do actions according to their importance (especially because of the influence map that gives informations of the current state of the map) : 
    - Create units, factories and turrets
    - Manage squads to make them capture or attack
    - Upgrade buildings
    - defend attacked buildings

## Launch
Launch RTSAI.exe

## Known Bugs
Sometimes when a unit die, there is this error :
MissingReferenceException: The object of type 'Unit' has been destroyed but you are still trying to access it.
Probably because we are trying to access a unit which is destroyed in the current frame