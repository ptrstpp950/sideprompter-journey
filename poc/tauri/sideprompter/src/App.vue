<script setup lang="ts">
import { ref } from "vue";
import { invoke } from "@tauri-apps/api/core";

import { WebviewWindow } from "@tauri-apps/api/webviewWindow";


const windowProtectionEnabled = ref(false);

async function protect(value: boolean) {
  try {
    const result = await invoke('set_window_protection', { enable: value });
    console.log("Window protection set to:", result);
    windowProtectionEnabled.value = value;
  } catch (e: any) {
    alert(e?.toString() || 'Unknown');
  }
}
function openNewWindow() {
  const webview = new WebviewWindow('my-label', {
    url: 'new-window.html'
  });
  webview.once('tauri://created', function () {
    webview.setContentProtected(true);
  });
  webview.once('tauri://error', function (e) {
    alert("Error creating webview: " + JSON.stringify(e));    
    // an error happened creating the webview
  });
}

</script>

<template>
  <main class="container">
    <h1>Welcome to SidePrompter</h1>

    <p>PoC verification #1</p>
    <div>
      <p class="row" style="margin-top: 1em; font-weight: bold;">
        {{ windowProtectionEnabled ?  "Now you don't!" : 'Now you see me' }}
      </p>
      <div class="button-row">
        <button @click="protect(true)">Enable window protection</button>
        <button @click="protect(false)">Disable window protection</button>
      </div>
    </div>
    <div>
      <p class="row">PoC verification #2</p>
      <div class="button-row">
        <button @click="openNewWindow" style="margin-top: 2em;">Open New Window</button>
      </div>
    </div>
  </main>
</template>

<style scoped>
.logo.vite:hover {
  filter: drop-shadow(0 0 2em #747bff);
}

.logo.vue:hover {
  filter: drop-shadow(0 0 2em #249b73);
}

</style>
<style>
:root {
  font-family: Inter, Avenir, Helvetica, Arial, sans-serif;
  font-size: 16px;
  line-height: 24px;
  font-weight: 400;

  color: #0f0f0f;
  background-color: #f6f6f6;

  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
  -webkit-text-size-adjust: 100%;
}

.container {
  margin: 0;
  padding-top: 10vh;
  display: flex;
  flex-direction: column;
  justify-content: center;
  text-align: center;
}

.logo {
  height: 6em;
  padding: 1.5em;
  will-change: filter;
  transition: 0.75s;
}

.logo.tauri:hover {
  filter: drop-shadow(0 0 2em #24c8db);
}

.row {
  display: flex;
  justify-content: center;
}

a {
  font-weight: 500;
  color: #646cff;
  text-decoration: inherit;
}

a:hover {
  color: #535bf2;
}

h1 {
  text-align: center;
}

input,
button {
  border-radius: 8px;
  border: 1px solid transparent;
  padding: 0.6em 1.2em;
  font-size: 1em;
  font-weight: 500;
  font-family: inherit;
  color: #0f0f0f;
  background-color: #ffffff;
  transition: border-color 0.25s;
  box-shadow: 0 2px 2px rgba(0, 0, 0, 0.2);
}

button {
  cursor: pointer;
}

button:hover {
  border-color: #396cd8;
}
button:active {
  border-color: #396cd8;
  background-color: #e8e8e8;
}

input,
button {
  outline: none;
}

#greet-input {
  margin-right: 5px;
}

.button-row {
  display: flex;
  justify-content: center;
  gap: 1em; /* optional: adds space between buttons */
}

@media (prefers-color-scheme: dark) {
  :root {
    color: #f6f6f6;
    background-color: #2f2f2f;
  }

  a:hover {
    color: #24c8db;
  }

  input,
  button {
    color: #ffffff;
    background-color: #0f0f0f98;
  }
  button:active {
    background-color: #0f0f0f69;
  }
}

</style>