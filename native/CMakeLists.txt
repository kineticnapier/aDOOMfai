cmake_minimum_required(VERSION 3.20)
project(adoom_native C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

set(DOOMGENERIC_DIR "${CMAKE_CURRENT_SOURCE_DIR}/third_party/doomgeneric/doomgeneric" CACHE PATH "Path to doomgeneric source directory")

if(NOT EXISTS "${DOOMGENERIC_DIR}/doomgeneric.c")
    message(FATAL_ERROR "doomgeneric source not found. Run fetch-doomgeneric.ps1 first.")
endif()

# IMPORTANT:
# Do not glob every .c file in doomgeneric. The tree also contains optional SDL,
# Allegro, X11, etc. backends. Those are not part of this embedded build.
# Keep this list aligned with upstream's generic core Makefile.
set(DOOM_SOURCES
    "${DOOMGENERIC_DIR}/dummy.c"
    "${DOOMGENERIC_DIR}/am_map.c"
    "${DOOMGENERIC_DIR}/doomdef.c"
    "${DOOMGENERIC_DIR}/doomstat.c"
    "${DOOMGENERIC_DIR}/dstrings.c"
    "${DOOMGENERIC_DIR}/d_event.c"
    "${DOOMGENERIC_DIR}/d_items.c"
    "${DOOMGENERIC_DIR}/d_iwad.c"
    "${DOOMGENERIC_DIR}/d_loop.c"
    "${DOOMGENERIC_DIR}/d_main.c"
    "${DOOMGENERIC_DIR}/d_mode.c"
    "${DOOMGENERIC_DIR}/d_net.c"
    "${DOOMGENERIC_DIR}/f_finale.c"
    "${DOOMGENERIC_DIR}/f_wipe.c"
    "${DOOMGENERIC_DIR}/g_game.c"
    "${DOOMGENERIC_DIR}/hu_lib.c"
    "${DOOMGENERIC_DIR}/hu_stuff.c"
    "${DOOMGENERIC_DIR}/info.c"
    "${DOOMGENERIC_DIR}/i_cdmus.c"
    "${DOOMGENERIC_DIR}/i_endoom.c"
    "${DOOMGENERIC_DIR}/i_joystick.c"
    "${DOOMGENERIC_DIR}/i_scale.c"
    "${DOOMGENERIC_DIR}/i_sound.c"
    "${DOOMGENERIC_DIR}/i_system.c"
    "${DOOMGENERIC_DIR}/i_timer.c"
    "${DOOMGENERIC_DIR}/memio.c"
    "${DOOMGENERIC_DIR}/m_argv.c"
    "${DOOMGENERIC_DIR}/m_bbox.c"
    "${DOOMGENERIC_DIR}/m_cheat.c"
    "${DOOMGENERIC_DIR}/m_config.c"
    "${DOOMGENERIC_DIR}/m_controls.c"
    "${DOOMGENERIC_DIR}/m_fixed.c"
    "${DOOMGENERIC_DIR}/m_menu.c"
    "${DOOMGENERIC_DIR}/m_misc.c"
    "${DOOMGENERIC_DIR}/m_random.c"
    "${DOOMGENERIC_DIR}/p_ceilng.c"
    "${DOOMGENERIC_DIR}/p_doors.c"
    "${DOOMGENERIC_DIR}/p_enemy.c"
    "${DOOMGENERIC_DIR}/p_floor.c"
    "${DOOMGENERIC_DIR}/p_inter.c"
    "${DOOMGENERIC_DIR}/p_lights.c"
    "${DOOMGENERIC_DIR}/p_map.c"
    "${DOOMGENERIC_DIR}/p_maputl.c"
    "${DOOMGENERIC_DIR}/p_mobj.c"
    "${DOOMGENERIC_DIR}/p_plats.c"
    "${DOOMGENERIC_DIR}/p_pspr.c"
    "${DOOMGENERIC_DIR}/p_saveg.c"
    "${DOOMGENERIC_DIR}/p_setup.c"
    "${DOOMGENERIC_DIR}/p_sight.c"
    "${DOOMGENERIC_DIR}/p_spec.c"
    "${DOOMGENERIC_DIR}/p_switch.c"
    "${DOOMGENERIC_DIR}/p_telept.c"
    "${DOOMGENERIC_DIR}/p_tick.c"
    "${DOOMGENERIC_DIR}/p_user.c"
    "${DOOMGENERIC_DIR}/r_bsp.c"
    "${DOOMGENERIC_DIR}/r_data.c"
    "${DOOMGENERIC_DIR}/r_draw.c"
    "${DOOMGENERIC_DIR}/r_main.c"
    "${DOOMGENERIC_DIR}/r_plane.c"
    "${DOOMGENERIC_DIR}/r_segs.c"
    "${DOOMGENERIC_DIR}/r_sky.c"
    "${DOOMGENERIC_DIR}/r_things.c"
    "${DOOMGENERIC_DIR}/sha1.c"
    "${DOOMGENERIC_DIR}/sounds.c"
    "${DOOMGENERIC_DIR}/statdump.c"
    "${DOOMGENERIC_DIR}/st_lib.c"
    "${DOOMGENERIC_DIR}/st_stuff.c"
    "${DOOMGENERIC_DIR}/s_sound.c"
    "${DOOMGENERIC_DIR}/tables.c"
    "${DOOMGENERIC_DIR}/v_video.c"
    "${DOOMGENERIC_DIR}/wi_stuff.c"
    "${DOOMGENERIC_DIR}/w_checksum.c"
    "${DOOMGENERIC_DIR}/w_file.c"
    "${DOOMGENERIC_DIR}/w_main.c"
    "${DOOMGENERIC_DIR}/w_wad.c"
    "${DOOMGENERIC_DIR}/z_zone.c"
    "${DOOMGENERIC_DIR}/w_file_stdc.c"
    "${DOOMGENERIC_DIR}/i_input.c"
    "${DOOMGENERIC_DIR}/i_video.c"
    "${DOOMGENERIC_DIR}/doomgeneric.c"
)

add_library(adoom_native SHARED
    ${DOOM_SOURCES}
    "${CMAKE_CURRENT_SOURCE_DIR}/adoom_platform.c"
)

target_include_directories(adoom_native PRIVATE "${DOOMGENERIC_DIR}")
target_compile_definitions(adoom_native PRIVATE
    DOOMGENERIC_RESX=320
    DOOMGENERIC_RESY=200
    _DEFAULT_SOURCE
)

if(MINGW)
    target_compile_options(adoom_native PRIVATE -O3 -std=gnu99 -fno-strict-aliasing)
    target_link_libraries(adoom_native PRIVATE m)
elseif(MSVC)
    message(FATAL_ERROR "This PoC currently targets MinGW-w64 GCC/Clang. Use MSYS2 UCRT64 or another MinGW-w64 toolchain.")
else()
    target_compile_options(adoom_native PRIVATE -O3 -fno-strict-aliasing)
    target_link_libraries(adoom_native PRIVATE m)
endif()

set_target_properties(adoom_native PROPERTIES
    PREFIX ""
    OUTPUT_NAME "adoom_native"
)
