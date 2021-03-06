#
# CMake Instructions for producing pwrshcommon.lib for consumption by powershell.exe and
# pwrshplugin.dll. It is a library that is statically compiled into each binary.
#
add_library(pwrshcommon
    pwrshcommon.cpp
    WinSystemCallFacade.cpp
    ConfigFileReader.cpp
    )

target_include_directories(pwrshcommon PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

if (BUILD_ONECORE)
    message("Building OneCore version of pwrshcommon.lib")
    # Libraries to use when creating this binary for Windows on OneCore-based SKUs
    set(PWRSHCOMMON_WINDOWS_LIBS 
        #        onecore.lib
        )
    set_target_properties(pwrshcommon PROPERTIES COMPILE_DEFINITIONS "CORECLR")
else () # NOT BUILD_ONECORE
    message("Building Full version of pwrshcommon.lib")
    # Libraries to use when creating this binary for Windows on full SKUs
    # Note: The appropriate libs get added automatically by VS
    set(PWRSHCOMMON_WINDOWS_LIBS
        # CoreCLR libs
###        ${STATIC_MT_CRT_LIB}
###        ${STATIC_MT_VCRT_LIB}
###        ${STATIC_UCRT_LIB}
        #
        #        ole32.lib
        #        oleaut32.lib
        #        uuid.lib
        #        user32.lib
        #        MUILoad.lib
        #        msxml6.lib
        #        mscoree.lib)
        )
endif (BUILD_ONECORE)

target_link_libraries(pwrshcommon
    ${PWRSHCOMMON_WINDOWS_LIBS})

