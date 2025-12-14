Here is a more reserved, technical introduction that details the specific functionality of the application.

# **Continuance**

> (Formerly *Bubbles07-Reborn*)

*Continuance* is a C# rewrite of *Bubbles-07*, a legacy project originally written in Python by [Guest257351](https://github.com/Guest257351). While originally Continuance was specifically on verifying accounts for *Operation: TCD* to bot with automated flinging inside [Criminality](https://www.roblox.com/games/4588604953/), Continuance has been expanded into a general-purpose Command Line Interface (CLI) for Roblox account management and automation.

The application is built on .NET 8 and utilizes *Spectre.Console* for the interface and *PuppeteerSharp* for browser automation. It serves as a lightweight, terminal-based alternative to GUI-heavy account managers.

### **Core Features**

#### **1. Multi-Instance & Game Launching**
Continuance includes a custom game launcher capable of handling multiple concurrent sessions.
*   **Mutex Bypass:** Automatically handles the `ROBLOX_singletonEvent` mutex, allowing an unlimited number of Roblox clients to run simultaneously on a single machine.
*   **Headless Mode:** An optional launch mode that utilizes Windows API calls (`user32.dll`) to hide and minimize game windows immediately upon launch. This reduces GPU rendering load when running mass instances.
*   **Launcher Detection:** Automatically scans for and utilizes third-party bootstrapper executables (Bloxstrap, Fishstrap) before defaulting to the standard Roblox installation.
*   **Follow User Protocol:** Implements the `roblox-player:1` launch protocol to possibly join a parent account. It requires the target to have their joins on. Will implement an auto-follow later on so it tests whether their joins are put on followers and friends.

#### **2. Account Management**
The tool provides methods for importing, validating, and maintaining account credentials.
*   **Browser Source Import:** Uses an integrated Chromium instance (Puppeteer) to capture `.ROBLOSECURITY` cookies. Users log in via the official Roblox login page, and the software intercepts the authenticated cookie automatically.
*   **Bulk Import/Export:** Supports importing cookies via text files or clipboard. Accounts can be exported based on specific criteria (e.g., valid cookies only, failed verification only).
*   **Token Refreshing:** Automatically handles XCSRF token acquisition and validation for API requests.

#### **3. Automation & Verification**
Several automated sequences are available to prepare accounts for gameplay or verification requirements.
*   **Profile Configuration:** Can scrape a target User ID and replicate their avatar assets (clothing, body colors, scales) and display name onto managed accounts. The account that is copying the targets avatar needs to have all required items in their inventory. 
*   **Friend Request Cycle:** Automates the process of sending and accepting friend requests between a batch of selected accounts to meet account age/social requirements. Used for games that have an alt account detection system.
*   **Badge Monitoring:** Launches specific Game IDs and polls the Roblox API to confirm when a required badge has been awarded, terminating the game process once the goal is met. Again, used for games that have an alt account detection system.
*   **Interactive Group Joining:** Automates navigation to group pages via the internal browser, pausing only for manual CAPTCHA solving.

#### **4. Application Configuration**
*   **Session Logging:** Automatically generates detailed, timestamped logs for every session in a local `Logs` directory.
*   **Rate Limit Handling:** Configurable delay settings for API requests and login attempts to avoid HTTP 429 errors.
*   **Theming:** Customizable color schemes for the CLI interface.

> Much love, Lush <3
