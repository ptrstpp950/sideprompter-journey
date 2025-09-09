import AppKit
import HotKey

let cmdAndQuestion = HotKey(key: .slash, modifiers: [.command])
let optAndQuestion = HotKey(key: .slash, modifiers: [.option])

cmdAndQuestion.keyDownHandler = {
    print("CMD+?")
}

optAndQuestion.keyDownHandler = {
    print("OPTION+?")
}

NSApplication.shared.run()
