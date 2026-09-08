#!/usr/bin/env bash
set -e

MP_URL=${MP_URL:-http://localhost:8080}
GT_URL=${GT_URL:-http://localhost:8081}

echo "1. MP 登录..."
LOGIN=$(curl -s -X POST "$MP_URL/api/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{
    "provider": "official",
    "app_id": "test_app",
    "device_id": "smoke-test",
    "auth_payload": { "username": "tester1", "password": "test1234" }
  }')

TOKEN=$(echo "$LOGIN" | jq -r .access_token)
if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then
  echo "登录失败: $LOGIN"
  exit 1
fi
echo "   Token 已拿到"

echo "2. game-user profile..."
curl -sf "$GT_URL/api/v1/user/profile?game_id=match3" \
  -H "Authorization: Bearer $TOKEN" | jq -c .

echo "3. 玩家状态（体力/金币/地图）..."
curl -sf "$GT_URL/api/v1/user/state?game_id=match3&map_id=1" \
  -H "Authorization: Bearer $TOKEN" | jq -c '{energy,energy_max,gold,unlocked_map,cleared_on_map,levels:(.levels|length)}'

echo "4. 通关 map1-level1..."
curl -sf -X POST "$GT_URL/api/v1/user/level/clear" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"game_id":"match3","map_id":1,"level_id":1,"stars":3,"steps":12,"score":3500}' | jq -c .

echo "5. 提交排行榜分数..."
curl -sf -X POST "$GT_URL/api/v1/leaderboard/score" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"game_id":"match3","board_id":"default","score":100,"nickname":"smoke"}' | jq -c .

echo "6. 查排行榜..."
curl -sf "$GT_URL/api/v1/leaderboard/top?game_id=match3&limit=5" | jq -c .

echo "7. 查自己排名..."
curl -sf "$GT_URL/api/v1/leaderboard/me?game_id=match3" \
  -H "Authorization: Bearer $TOKEN" | jq -c .

echo "8. 错误 Token 应返回 401..."
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  "$GT_URL/api/v1/user/profile?game_id=match3" \
  -H "Authorization: Bearer invalid-token")
[ "$CODE" = "401" ] || { echo "期望 401，实际 $CODE"; exit 1; }

echo ""
echo "✅ 全部通过"
