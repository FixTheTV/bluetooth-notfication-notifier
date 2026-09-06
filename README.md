# \# BLUETOOTH NOTIFIER


# So I got myself into a weirdly specific phone situation.


# My new phone cannot use mobile data. I honestly still do not know why. My old phone can, so I ended up putting my SIM card back into the old phone and using it as a hotspot for the new one.


# That technically works, but it creates one very annoying problem: all the important SMS messages and phone calls now arrive on the old phone.


# And my old phone has another problem. Half of the screen is covered by screen fluid damage. It is still alive, but using it is such a pain.



# So instead of going to a repair shop to fix either the old phone’s screen or the new phone’s mobile data issue, I decided to build the most "necessary" solution possible:


# I made an Android app that forwards incoming notifications from the old phone to the new phone over Bluetooth.


# Oh yeah, I vibe coded this.


## The Jargon Section


# The app is a native Android app written in C# using .NET for Android. It has two modes:


# \- \*\*Sender Mode\*\*, for the old phone

# \- \*\*Receiver Mode\*\*, for the new phone


# On the old phone, the app runs a `NotificationListenerService`. This lets it listen for posted Android notifications, including SMS and call notifications. When a notification appears, the app extracts the app name, title, text, and timestamp, then serializes that into JSON.


# The phones communicate over classic Bluetooth using RFCOMM with the standard Serial Port Profile UUID:


# `00001101-0000-1000-8000-00805F9B34FB`


# The old phone opens a Bluetooth server socket and waits for the new phone to connect. The new phone runs a foreground service that connects to the old phone, keeps the connection alive, reads incoming JSON messages, and turns them back into local Android notifications.


# So the flow is basically:


# `Old phone notification -> JSON payload -> Bluetooth RFCOMM stream -> New phone foreground service -> Local notification`


# It is not cloud-based, does not require internet, and does not need the two phones to be on the same Wi-Fi network. As long as Bluetooth works and the devices are paired, the new phone can receive alerts from the old one.

# 

# 

