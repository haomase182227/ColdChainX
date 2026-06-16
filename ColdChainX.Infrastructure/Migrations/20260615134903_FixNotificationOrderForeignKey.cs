using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixNotificationOrderForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.table_constraints
                        WHERE constraint_schema = 'public'
                          AND table_name = 'notifications'
                          AND constraint_name = 'fk_noti_order'
                          AND constraint_type = 'FOREIGN KEY'
                    ) THEN
                        ALTER TABLE public.notifications DROP CONSTRAINT fk_noti_order;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public'
                          AND table_name = 'transport_orders'
                    ) THEN
                        UPDATE public.notifications n
                        SET order_id = NULL
                        WHERE n.order_id IS NOT NULL
                          AND NOT EXISTS (
                              SELECT 1
                              FROM public.transport_orders o
                              WHERE o.order_id = n.order_id
                          );

                        ALTER TABLE public.notifications
                        ADD CONSTRAINT fk_noti_order
                        FOREIGN KEY (order_id)
                        REFERENCES public.transport_orders(order_id)
                        ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.table_constraints
                        WHERE constraint_schema = 'public'
                          AND table_name = 'notifications'
                          AND constraint_name = 'fk_noti_order'
                          AND constraint_type = 'FOREIGN KEY'
                    ) THEN
                        ALTER TABLE public.notifications DROP CONSTRAINT fk_noti_order;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public'
                          AND table_name = 'transport_order'
                    ) THEN
                        ALTER TABLE public.notifications
                        ADD CONSTRAINT fk_noti_order
                        FOREIGN KEY (order_id)
                        REFERENCES public.transport_order(order_id)
                        ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }
    }
}
