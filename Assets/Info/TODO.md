CSS 385  
Kenneth Angeles, Vincent Huang  
5/4/26

Where we're at:

* Able to move boat and raise sail  
  * Kenneth was able to understand the basics of the Boat/Sail Controller  
  * Boat (moves left and right (a and d), wind moves it up and down)  
  * Sail (move up and down based on the wind)

What we plan to do / **Goals by next class**:

* High priority:  
  * Get initial water tiles  
  * Make boat turning look good (Vincent)  
    * Add more comments to Boat/SailController (just to get done soon)  
  * Add tiles for the water. Water animations procedurally animated.  
  * Research procedural generation (Kenneth). Explain by next class (Wednesday), implement by end of the week (Monday).
    * Cellular automata  
      * **Two states: still and non-still**
        * **Radius, double the length of the player's (boat) view**
        * **Ripples**  
          * **Based on anything moving (boat, mobs, fish, etc.)**  
        * **Waves**  
          * **Based on wind**  
      * Two layers (separate tiles)  
        * Base layer is just blue with a few white lines representing still water  
        * Upper layer are the actual waves  
    * Generate islands (Vernoi \+ Perlin noise)  
* Medium priority:  
  * Fish sprites (Kenneth) (16x16)  
    * Could steal from someone  
  * Fishing hook sprites (Kenneth)  
    * Could steal from someone  
  * Game balance discussion (Speed, health, weight capacity, weapons, pricing)  
  * Add wind so the boat can move.  
* Low priority (but we need to get to them eventually):  
  * Figure out how to randomly place fish  
  * Player mechanics (Hunger, Weight)  
  * UI  
    * Map system  
    * Compass system  
    * Island \+ Shopping  
  * Enemy sprites  
  * Enemy behavior  
  * Night and day system  
  * Weapons: Cannons, harpoons  
    * Lighting  
    * Inventory system  
  * Pricing  
  * SFX  
  * Background music  
  * Particle system when catching fish  
    * Splashing  
  * Adding custom decals to the sail

Sprites

* High difficulty  
  * Boat  
  * Enemies  
  * Island terrain/elevation  
    * Assembling island sprites  
* Medium difficulty  
  * Item sprites (Vincent \+ Kenneth) CAN BE REFERENCED/TRACED  
    * Weapons  
      * Guns  
      * Musket  
    * Food  
      * Bread  
      * Hardtack  
      * Fish  
        * Cooked/uncooked fish sprites (Kenneth)  
          * Having different variants  
            * Layering  
          *   
      * Fire sprite  
    * Enemies (certain types can be food)  
      * Octopus (f)  
      * Shark (f)  
      * Sea serpent  
* Currency: Gold coins  
* Obstacles: Rocks  
* One single treasure chest.  
* Low  
  * Assembling island sprites (Kenneth)
