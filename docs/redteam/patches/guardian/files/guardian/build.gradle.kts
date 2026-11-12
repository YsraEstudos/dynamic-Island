plugins {
    // AGP 9 tem Kotlin embutido (o módulo :app também não aplica kotlin-android); este módulo segue o mesmo padrão.
    alias(libs.plugins.android.application)
}

// Segundo pacote (guardião) do bloqueio. Ele não tem UI de produto, Compose nem Firebase: o objetivo é
// manter um processo próprio vivo fora do app principal e reviver a acessibilidade depois de um force-stop.
android {
    namespace = "com.example.focusapp.guardian"
    compileSdk = 36
    defaultConfig {
        applicationId = "com.example.focusapp.guardian"
        // Mesmo parque do app principal: moto g(20) Android 11 (SDK 30) e S23 Ultra Android 14.
        minSdk = 30
        targetSdk = 35
        versionCode = 2
        versionName = "1.0.1"
    }

    signingConfigs {
        getByName("debug") {
            // Mesma chave do app principal: as permissions de assinatura (PROTECTION_CONTROL e
            // GUARDIAN_CONTROL) só valem entre os dois APKs se a assinatura for idêntica.
            // Caminho relativo ao módulo guardian/ -> app/debug.keystore na raiz do repo.
            storeFile = file("../app/debug.keystore")
            storePassword = "android"
            keyAlias = "androiddebugkey"
            keyPassword = "android"
        }
    }

    buildTypes {
        debug {
            signingConfig = signingConfigs.getByName("debug")
        }
        release {
            // APK interno instalado por ADB, sem ofuscação: menos peças para dar errado em campo.
            isMinifyEnabled = false
            isShrinkResources = false
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    // APK nunca vai para loja: o lint do release não deve ser capaz de quebrar o build do guardião.
    lint {
        checkReleaseBuilds = false
        abortOnError = false
    }
}

kotlin {
    jvmToolchain(17)
}

