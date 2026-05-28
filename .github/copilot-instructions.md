# Copilot Instructions

## Рекомендации по проекту
- В проекте сборка Game.Core.Ecs.Components не должна знать ни о каких других игровых сборках, только о сервисах (типа EnumService). Если нужна ссылка на MonoBehaviour — хранить GameObject, а GetComponent делать уже в системах (Game.Core.Ecs.Systems), которые могут знать о других сборках.