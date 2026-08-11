import os

import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib


class StatusNotifierWatcher(dbus.service.Object):
    def __init__(self, bus):
        super().__init__(bus, "/StatusNotifierWatcher")

    @dbus.service.method(
        "org.kde.StatusNotifierWatcher", in_signature="s", out_signature=""
    )
    def RegisterStatusNotifierItem(self, service):
        with open(os.environ["BUDDY_TRAY_CAPTURE"], "w", encoding="utf-8") as stream:
            stream.write(str(service))


DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
name = dbus.service.BusName(
    "org.kde.StatusNotifierWatcher", bus=bus, do_not_queue=True
)
watcher = StatusNotifierWatcher(bus)
with open(os.environ["BUDDY_TRAY_READY"], "w", encoding="utf-8") as stream:
    stream.write("ready")
GLib.MainLoop().run()
