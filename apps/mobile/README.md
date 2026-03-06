# NSFinTech Mobile

Expo React Native app scaffold for the NSFinTech end-user mobile client.

## Local run

1. `cp .env.example .env`
2. Set `EXPO_PUBLIC_API_BASE_URL` for your device:
   - iOS simulator: `http://192.168.0.11:5080`
   - Android emulator: `http://10.0.2.2:5080`
   - Physical device: `http://<YOUR_PC_LAN_IP>:5080`
3. `pnpm install`
4. `pnpm start`
