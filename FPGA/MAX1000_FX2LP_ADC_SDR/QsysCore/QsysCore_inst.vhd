	component QsysCore is
		port (
			clk_clk                                                                                         : in    std_logic                     := 'X'; -- clk
			pio_0_external_connection_export                                                                : out   std_logic_vector(31 downto 0);        -- export
			pio_1_external_connection_export                                                                : out   std_logic_vector(31 downto 0);        -- export
			pio_2_external_connection_export                                                                : out   std_logic_vector(31 downto 0);        -- export
			reset_reset_n                                                                                   : in    std_logic                     := 'X'; -- reset_n
			spi_slave_to_avalon_mm_master_bridge_0_export_0_mosi_to_the_spislave_inst_for_spichain          : in    std_logic                     := 'X'; -- mosi_to_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_nss_to_the_spislave_inst_for_spichain           : in    std_logic                     := 'X'; -- nss_to_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_miso_to_and_from_the_spislave_inst_for_spichain : inout std_logic                     := 'X'; -- miso_to_and_from_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_sclk_to_the_spislave_inst_for_spichain          : in    std_logic                     := 'X'; -- sclk_to_the_spislave_inst_for_spichain
			pio_3_external_connection_export                                                                : out   std_logic_vector(31 downto 0);        -- export
			pio_4_external_connection_export                                                                : out   std_logic_vector(31 downto 0)         -- export
		);
	end component QsysCore;

	u0 : component QsysCore
		port map (
			clk_clk                                                                                         => CONNECTED_TO_clk_clk,                                                                                         --                                             clk.clk
			pio_0_external_connection_export                                                                => CONNECTED_TO_pio_0_external_connection_export,                                                                --                       pio_0_external_connection.export
			pio_1_external_connection_export                                                                => CONNECTED_TO_pio_1_external_connection_export,                                                                --                       pio_1_external_connection.export
			pio_2_external_connection_export                                                                => CONNECTED_TO_pio_2_external_connection_export,                                                                --                       pio_2_external_connection.export
			reset_reset_n                                                                                   => CONNECTED_TO_reset_reset_n,                                                                                   --                                           reset.reset_n
			spi_slave_to_avalon_mm_master_bridge_0_export_0_mosi_to_the_spislave_inst_for_spichain          => CONNECTED_TO_spi_slave_to_avalon_mm_master_bridge_0_export_0_mosi_to_the_spislave_inst_for_spichain,          -- spi_slave_to_avalon_mm_master_bridge_0_export_0.mosi_to_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_nss_to_the_spislave_inst_for_spichain           => CONNECTED_TO_spi_slave_to_avalon_mm_master_bridge_0_export_0_nss_to_the_spislave_inst_for_spichain,           --                                                .nss_to_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_miso_to_and_from_the_spislave_inst_for_spichain => CONNECTED_TO_spi_slave_to_avalon_mm_master_bridge_0_export_0_miso_to_and_from_the_spislave_inst_for_spichain, --                                                .miso_to_and_from_the_spislave_inst_for_spichain
			spi_slave_to_avalon_mm_master_bridge_0_export_0_sclk_to_the_spislave_inst_for_spichain          => CONNECTED_TO_spi_slave_to_avalon_mm_master_bridge_0_export_0_sclk_to_the_spislave_inst_for_spichain,          --                                                .sclk_to_the_spislave_inst_for_spichain
			pio_3_external_connection_export                                                                => CONNECTED_TO_pio_3_external_connection_export,                                                                --                       pio_3_external_connection.export
			pio_4_external_connection_export                                                                => CONNECTED_TO_pio_4_external_connection_export                                                                 --                       pio_4_external_connection.export
		);

