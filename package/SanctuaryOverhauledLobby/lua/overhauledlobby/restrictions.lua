-- Independently implemented lobby-owned rules. Native Tags are queried at use
-- time: never cache an answer before all selected faction templates are loaded.
local settings = Import("modoptions/com.sanctuary.overhauled-lobby.lua").Options
local explicit = settings.units or {}
Enabled = settings.noLand or settings.noAir or settings.noNaval or settings.noExperimental or next(explicit) ~= nil
function Denied(id)
    if type(id) ~= "string" then return false end
    if explicit[id] then return true end
    local function has(name)
        local group = Tags and Tags[name]
        return group and group[id] and true or false
    end
    if settings.noExperimental and has("TECH4") then return true end
    if has("COMMAND") or has("ENGINEER") or not has("MOBILE") then return false end
    if settings.noLand and has("LAND") then return true end
    if settings.noAir and has("AIR") then return true end
    return settings.noNaval and has("NAVAL") or false
end
function ConfigureAI(options)
    if not options then return end
    local copy = {}
    for key,value in pairs(options.useLayer or {}) do copy[key] = value end
    if settings.noLand then copy.land = false end
    if settings.noAir then copy.air = false end
    if settings.noNaval then copy.water = false end
    options.useLayer = copy
end
