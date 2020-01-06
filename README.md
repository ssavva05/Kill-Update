1) use cleanUpdates.bad
2) cntr+R and add shell:startup 
3) copy paste the kill.exe
4) right click properties click run as admin box
5) Right click run it as admin

# Kill-Update
Prevents Windows 10 from updating. Can be manually disabled when updating is convenient.

# Using the program
Copy [the latest release](https://github.com/dlebansais/Kill-Update/releases/download/v1.0.92/KillUpdate.exe) in a directory, then run it as administrator. This will create a little icon in the task bar.

Right-click the icon to pop a menu with the following items:

- Load at startup. When checked, the application is loaded when a user logs in.
- Locked. When checked, the application is preventing Windows from upgrading.
- Exit

# How does it work?
Every 10 seconds, this application checks the Windows Update service, and if not disabled, disables it.

# Manually upgrading Windows
To upgrade manually, first uncheck the Locked menu (see above), then open Windows settings and check for updates. When they have been installed and the computer has rebooted, you can check the Locked menu again.

# Log file (optional)

You can log activity to a file. To turn on logging:

+ Create a text file called `settings.txt` in the same folder as the program.
+ In this file, add a line with the path to the log file. You can choose the destination folder and file name.
+ Start the program. This should immediately add a few lines to the log file.

Note that this file grows with time, but slowly.

If you have an issue with this program, you can add the log file to your bug report.

# Screenshots

![Menu](/Screenshots/Menu.png?raw=true "The app menu")

# Certification
This program is digitally signed with a [CAcert](https://www.cacert.org/) certificate.
