# Bot Language Documentation

## Overview
Bot is a command-based language designed to streamline interactions with the CryptoBlade system and its automation. Commands follow a structured format:

bot {command} {options}

This language enables the user to execute specific actions, retrieve system statuses, and manage the CryptoBlade project efficiently.

## Syntax Rules
- Commands must start with `bot`.
- Options are prefixed with `--` and can modify command behavior.
- Arguments can be provided after the command to specify additional parameters.

---

## Command List

### 1. **Project Management**
#### Retrieve and manage project status
- **bot show status**  
  Displays the current status of the CryptoBlade project from bot_notes.md file.
- **bot show status --last**  
  Shows the most recent saved project state.
- **bot save status**  
  Saves the current project state to `bot_notes.md`.

### 2. **Strategy Management**
#### Control trading strategies
- **bot strategy list**  
  Lists all available trading strategies.
- **bot strategy start {name}**  
  Starts the specified trading strategy.
- **bot strategy stop {name}**  
  Stops the specified trading strategy.
- **bot strategy optimize {name}**  
  Optimizes the parameters for the specified strategy.
- **bot strategy backtest {name}**  
  Runs a backtest for the given strategy.

### 3. **System Health Checks**
#### Monitor system health
- **bot health check**  
  Performs a general system health check.
- **bot health check --trading**  
  Checks the status of the live trading system.
- **bot health check --backtest**  
  Checks the status of the backtesting system.

### 4. **Exchange Interaction**
#### Manage API connections and orders
- **bot exchange status**  
  Displays connection status for Bybit and Binance.
- **bot exchange reconnect**  
  Re-establishes API connections.
- **bot order list**  
  Retrieves a list of active orders.
- **bot order cancel {order_id}**  
  Cancels a specific order by ID.

### 5. **Configuration Management**
#### Adjust system settings dynamically
- **bot config show**  
  Displays the current configuration.
- **bot config set {key} {value}**  
  Updates a configuration setting dynamically.
- **bot config reset**  
  Restores default configurations.

### 6. **Optimization and Performance**
#### Run optimization processes
- **bot optimizer start**  
  Initiates an optimization session using genetic algorithms.
- **bot optimizer status**  
  Displays the current optimizer progress.
- **bot optimizer stop**  
  Halts an ongoing optimization session.

### 7. **Logging and Debugging**
#### Access and manage system logs
- **bot logs show**  
  Displays the latest logs.
- **bot logs clear**  
  Clears the log files.
- **bot debug mode {on|off}**  
  Enables or disables debug mode.

### 8. **Output Format Conversion**
#### Change output format
- **bot convert --md**  
  Outputs the content in Markdown format.
- **bot convert --json**  
  Outputs the content in JSON format.
- **bot convert --txt**  
  Outputs the content in plain text format.

### 9. **Documentation**
#### Update and display project documentation
- **bot save docs**  
  Updates all relevant documentation files (BotLanguage.md, GettingStarted.md, Parameters.md, Strategies.md) with any new knowledge gained in the session. This includes modifications to strategies, parameter settings, or any new commands. Once updated, the new or updated docs can be displayed or saved as needed.

---

## Integration with Asystent (ChatGPT)
Asystent must read and learn the Bot language upon startup and apply the defined commands accordingly. If new commands are introduced during a session, they must be added to this documentation at the end of the chat.

---

## Future Enhancements
- Support for batch execution of commands.
- Additional debugging and diagnostic tools.
- Custom user-defined commands for specialized workflows.
